using System.Reflection;
using System.Text.Json;

namespace LotroPresence.Bridge;

internal static class Program
{
    private const string MutexName = @"Local\Dusk.LotroPresence.Bridge";

    public static async Task<int> Main(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            return RunSelfTests();
        }

        AppLog.Info($"Démarrage LotroPresence {GetInformationalVersion()}.");

        try
        {
            using var mutex = new Mutex(true, MutexName, out var createdNew);
            if (!createdNew)
            {
                AppLog.Info("Une autre instance du bridge est déjà active.");
                return 0;
            }

            var config = BridgeConfigLoader.Load();
            if (config is null)
            {
                AppLog.Error("Démarrage annulé : configuration invalide.");
                return 2;
            }

            var applicationId = Environment.GetEnvironmentVariable("LOTROPRESENCE_DISCORD_APP_ID");
            if (string.IsNullOrWhiteSpace(applicationId))
            {
                applicationId = config.DiscordApplicationId;
            }

            if (!BridgeConfigLoader.IsValidDiscordApplicationId(applicationId))
            {
                AppLog.Error("Démarrage annulé : DiscordApplicationId invalide.");
                return 2;
            }

            var pluginDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "The Lord of the Rings Online",
                "PluginData");

            LotroLifecycleMonitor.Start();

            using var discord = new DiscordRpcClient(applicationId);
            if (!discord.Initialize())
            {
                AppLog.Error("Démarrage annulé : impossible d'initialiser Discord RPC.");
                return 3;
            }

            FileInfo? selectedFile = null;
            string? lastPresenceKey = null;
            var nextDiscoveryUtc = DateTime.MinValue;
            var selectedWriteTimeUtc = DateTime.MinValue;
            long selectedLength = -1;
            PresenceSnapshot? selectedSnapshot = null;
            var presenceVisible = false;

            try
            {
                while (!LotroLifecycleMonitor.ExitRequested)
                {
                    if (!LotroLifecycleMonitor.IsLotroRunning)
                    {
                        if (presenceVisible)
                        {
                            discord.SetPresence(null);
                            presenceVisible = false;
                            lastPresenceKey = null;
                        }

                        selectedFile = null;
                        nextDiscoveryUtc = DateTime.MinValue;
                        selectedWriteTimeUtc = DateTime.MinValue;
                        selectedLength = -1;
                        selectedSnapshot = null;

                        await Delay(config.PollIntervalMilliseconds);
                        continue;
                    }

                    var nowUtc = DateTime.UtcNow;
                    var snapshot = PluginDataReader.ReadSelectedSnapshot(
                        ref selectedFile,
                        ref nextDiscoveryUtc,
                        config.HeartbeatTimeoutSeconds,
                        ref selectedWriteTimeUtc,
                        ref selectedLength,
                        ref selectedSnapshot);

                    if (nowUtc >= nextDiscoveryUtc)
                    {
                        var discovered = PluginDataReader.FindLatestActiveSnapshot(
                            pluginDataRoot,
                            config.HeartbeatTimeoutSeconds);

                        if (discovered is not null)
                        {
                            selectedFile = new FileInfo(discovered.FilePath);
                            selectedFile.Refresh();
                            selectedWriteTimeUtc = selectedFile.Exists
                                ? selectedFile.LastWriteTimeUtc
                                : DateTime.MinValue;
                            selectedLength = selectedFile.Exists ? selectedFile.Length : -1;
                            selectedSnapshot = discovered;
                            snapshot = discovered;
                        }

                        nextDiscoveryUtc = nowUtc.AddSeconds(
                            selectedFile is null
                                ? 2
                                : config.DiscoveryIntervalSeconds);
                    }

                    var sessionStart = LotroLifecycleMonitor.SessionStartedUtc;
                    if (sessionStart is null)
                    {
                        await Delay(config.PollIntervalMilliseconds);
                        continue;
                    }

                    if (snapshot is { Active: true })
                    {
                        var key = PresenceFactory.BuildPresenceKey(snapshot, sessionStart.Value);
                        if (!string.Equals(key, lastPresenceKey, StringComparison.Ordinal) &&
                            discord.SetPresence(
                                PresenceFactory.BuildPresence(snapshot, config, sessionStart.Value)))
                        {
                            lastPresenceKey = key;
                            presenceVisible = true;
                        }
                    }
                    else
                    {
                        var key = $"selection|{sessionStart.Value.Ticks}";
                        if (!string.Equals(key, lastPresenceKey, StringComparison.Ordinal) &&
                            discord.SetPresence(
                                PresenceFactory.BuildSelectionPresence(config, sessionStart.Value)))
                        {
                            lastPresenceKey = key;
                            presenceVisible = true;
                        }
                    }

                    await Delay(config.PollIntervalMilliseconds);
                }
            }
            finally
            {
                discord.SetPresence(null);
                GC.KeepAlive(mutex);
            }

            AppLog.Info("LotroPresence arrêté proprement.");
            return 0;
        }
        catch (Exception exception)
        {
            AppLog.Error("Erreur fatale du bridge.", exception);
            return 1;
        }
    }

    private static Task Delay(int milliseconds) =>
        Task.Delay(Math.Clamp(milliseconds, 500, 30000));

    private static string GetInformationalVersion() =>
        typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "inconnue";

    private static int RunSelfTests()
    {
        try
        {
            var beorning = new PresenceSnapshot(
                "test", 4, "Heimvald", 28, 214, "Béornide",
                114, "Béornide", 1, true, "Orcrist");
            var champion = new PresenceSnapshot(
                "test", 4, "Altherian", 28, 172, "Champion",
                23, "Homme", 1, true, "Orcrist");
            var start = new DateTime(638000000000000000, DateTimeKind.Utc);
            var selection = PresenceFactory.BuildSelectionPresence(new BridgeConfig(), start);
            var mergedConfig = new BridgeConfig
            {
                DiscordApplicationId = "1550150231092502613",
                PollIntervalMilliseconds = 2000,
                DiscoveryIntervalSeconds = 10
            };
            using var overrides = JsonDocument.Parse(
                """{"PollIntervalMilliseconds":1500}""");
            var warnings = new List<string>();
            var errors = new List<string>();
            var overrideOk = BridgeConfigLoader.ApplyOverrides(
                mergedConfig,
                overrides.RootElement,
                warnings,
                errors);

            var ok =
                PresenceFactory.BuildDetails(beorning) == "Heimvald • Béornide • Niveau 28" &&
                PresenceFactory.BuildState(beorning) == "Solo • Serveur Orcrist" &&
                PresenceFactory.GetClassAssetKey(beorning.ClassName) == "class_beorning" &&
                PresenceFactory.BuildDetails(champion) == "Altherian • Homme • Champion • Niveau 28" &&
                PresenceFactory.BuildState(champion) == "Solo • Serveur Orcrist" &&
                PresenceFactory.GetClassAssetKey(champion.ClassName) == "class_champion" &&
                selection.Details == "Sélection de personnage" &&
                string.IsNullOrWhiteSpace(selection.State) &&
                selection.Timestamps?.Start == start &&
                selection.Assets?.SmallImageKey == PresenceFactory.SelectionAssetKey &&
                overrideOk &&
                errors.Count == 0 &&
                mergedConfig.DiscordApplicationId == "1550150231092502613" &&
                mergedConfig.PollIntervalMilliseconds == 1500 &&
                mergedConfig.DiscoveryIntervalSeconds == 10;

            return ok ? 0 : 1;
        }
        catch
        {
            return 1;
        }
    }
}
