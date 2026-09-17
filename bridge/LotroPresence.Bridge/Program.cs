using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DiscordRPC;

namespace LotroPresence.Bridge;

internal sealed class BridgeConfig
{
    public string DiscordApplicationId { get; set; } = string.Empty;
    public string LargeImageKey { get; set; } = string.Empty;
    public string LargeImageText { get; set; } = "The Lord of the Rings Online";
    public int HeartbeatTimeoutSeconds { get; set; } = 50;
    public int PollIntervalMilliseconds { get; set; } = 2000;
    public int DiscoveryIntervalSeconds { get; set; } = 10;
}

internal sealed record PresenceSnapshot(
    string FilePath,
    int SchemaVersion,
    string Character,
    int Level,
    int ClassId,
    string ClassName,
    int RaceId,
    string RaceName,
    int PartySize,
    bool Active,
    string ServerName);

internal static class Program
{
    private const int SupportedSchemaVersion = 4;
    private const string MutexName = @"Local\Dusk.LotroPresence.Bridge";
    private const string SelectionAssetKey = "character_select";

    private static readonly Regex EntryRegex = new(
        """\["(?<key>[^"]+)"\]\s*=\s*(?<value>"(?:\\.|[^"])*"|true|false|-?\d+(?:\.\d+)?)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> ReportedSchemaWarnings =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> ClassAssetKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Béornide"] = "class_beorning",
            ["Bagarreur"] = "class_brawler",
            ["Cambrioleur"] = "class_burglar",
            ["Capitaine"] = "class_captain",
            ["Champion"] = "class_champion",
            ["Gardien"] = "class_guardian",
            ["Chasseur"] = "class_hunter",
            ["Maître du savoir"] = "class_loremaster",
            ["Marin"] = "class_mariner",
            ["Ménestrel"] = "class_minstrel",
            ["Gardien des runes"] = "class_runekeeper",
            ["Sentinelle"] = "class_warden"
        };

    public static async Task<int> Main(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            return RunSelfTests();
        }

        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            return 0;
        }

        var config = LoadConfig();
        if (config is null)
        {
            return 2;
        }

        var applicationId = Environment.GetEnvironmentVariable("LOTROPRESENCE_DISCORD_APP_ID");
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            applicationId = config.DiscordApplicationId;
        }

        if (string.IsNullOrWhiteSpace(applicationId) ||
            applicationId.Contains("PUT_DISCORD", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        var pluginDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "The Lord of the Rings Online",
            "PluginData");

        using var discord = new DiscordRpcClient(applicationId);
        discord.Initialize();

        FileInfo? selectedFile = null;
        string? lastPresenceKey = null;
        var nextDiscoveryUtc = DateTime.MinValue;

        try
        {
            while (!LotroLifecycleMonitor.ExitRequested)
            {
                var nowUtc = DateTime.UtcNow;
                var snapshot = ReadSelectedSnapshot(
                    ref selectedFile,
                    ref nextDiscoveryUtc,
                    config.HeartbeatTimeoutSeconds);

                if (nowUtc >= nextDiscoveryUtc)
                {
                    var discovered = FindLatestActiveSnapshot(
                        pluginDataRoot,
                        config.HeartbeatTimeoutSeconds);

                    if (discovered is not null)
                    {
                        selectedFile = new FileInfo(discovered.FilePath);
                        snapshot = discovered;
                    }

                    nextDiscoveryUtc = nowUtc.AddSeconds(
                        selectedFile is null
                            ? 2
                            : Math.Clamp(config.DiscoveryIntervalSeconds, 5, 120));
                }

                var sessionStart = LotroLifecycleMonitor.SessionStartedUtc;
                if (sessionStart is not null)
                {
                    if (snapshot is { Active: true })
                    {
                        var key = BuildPresenceKey(snapshot, sessionStart.Value);
                        if (!string.Equals(key, lastPresenceKey, StringComparison.Ordinal))
                        {
                            discord.SetPresence(BuildPresence(snapshot, config, sessionStart.Value));
                            lastPresenceKey = key;
                        }
                    }
                    else
                    {
                        var key = $"selection|{sessionStart.Value.Ticks}";
                        if (!string.Equals(key, lastPresenceKey, StringComparison.Ordinal))
                        {
                            discord.SetPresence(BuildSelectionPresence(config, sessionStart.Value));
                            lastPresenceKey = key;
                        }
                    }
                }

                await Task.Delay(Math.Clamp(config.PollIntervalMilliseconds, 500, 30000));
            }
        }
        finally
        {
            discord.SetPresence(null);
            GC.KeepAlive(mutex);
        }

        return 0;
    }

    private static PresenceSnapshot? ReadSelectedSnapshot(
        ref FileInfo? selectedFile,
        ref DateTime nextDiscoveryUtc,
        int timeoutSeconds)
    {
        if (selectedFile is null)
        {
            return null;
        }

        try
        {
            selectedFile.Refresh();
            if (!selectedFile.Exists || !IsFresh(selectedFile, timeoutSeconds))
            {
                selectedFile = null;
                nextDiscoveryUtc = DateTime.MinValue;
                return null;
            }

            var snapshot = TryReadSnapshot(selectedFile.FullName);
            if (snapshot is { Active: true })
            {
                return snapshot;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        selectedFile = null;
        nextDiscoveryUtc = DateTime.MinValue;
        return null;
    }

    private static BridgeConfig? LoadConfig()
    {
        var localPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        var defaultPath = Path.Combine(AppContext.BaseDirectory, "config.default.json");

        try
        {
            if (!File.Exists(defaultPath))
            {
                return null;
            }

            var config = JsonSerializer.Deserialize<BridgeConfig>(
                File.ReadAllText(defaultPath, Encoding.UTF8),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (config is null)
            {
                return null;
            }

            if (!File.Exists(localPath))
            {
                return config;
            }

            using var localDocument = JsonDocument.Parse(
                File.ReadAllText(localPath, Encoding.UTF8));
            if (localDocument.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            ApplyConfigOverrides(config, localDocument.RootElement);
            return config;
        }
        catch
        {
            return null;
        }
    }

    private static void ApplyConfigOverrides(BridgeConfig config, JsonElement root)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals("DiscordApplicationId", StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                config.DiscordApplicationId = property.Value.GetString() ?? string.Empty;
            }
            else if (property.Name.Equals("LargeImageKey", StringComparison.OrdinalIgnoreCase) &&
                     property.Value.ValueKind == JsonValueKind.String)
            {
                config.LargeImageKey = property.Value.GetString() ?? string.Empty;
            }
            else if (property.Name.Equals("LargeImageText", StringComparison.OrdinalIgnoreCase) &&
                     property.Value.ValueKind == JsonValueKind.String)
            {
                config.LargeImageText = property.Value.GetString() ?? string.Empty;
            }
            else if (property.Name.Equals("HeartbeatTimeoutSeconds", StringComparison.OrdinalIgnoreCase) &&
                     property.Value.ValueKind == JsonValueKind.Number &&
                     property.Value.TryGetInt32(out var heartbeatTimeoutSeconds))
            {
                config.HeartbeatTimeoutSeconds = heartbeatTimeoutSeconds;
            }
            else if (property.Name.Equals("PollIntervalMilliseconds", StringComparison.OrdinalIgnoreCase) &&
                     property.Value.ValueKind == JsonValueKind.Number &&
                     property.Value.TryGetInt32(out var pollIntervalMilliseconds))
            {
                config.PollIntervalMilliseconds = pollIntervalMilliseconds;
            }
            else if (property.Name.Equals("DiscoveryIntervalSeconds", StringComparison.OrdinalIgnoreCase) &&
                     property.Value.ValueKind == JsonValueKind.Number &&
                     property.Value.TryGetInt32(out var discoveryIntervalSeconds))
            {
                config.DiscoveryIntervalSeconds = discoveryIntervalSeconds;
            }
        }
    }

    private static PresenceSnapshot? FindLatestActiveSnapshot(string root, int timeoutSeconds)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        try
        {
            foreach (var file in EnumeratePluginDataFilesSafe(root)
                         .Where(file => IsFresh(file, timeoutSeconds))
                         .OrderByDescending(file => file.LastWriteTimeUtc))
            {
                var snapshot = TryReadSnapshot(file.FullName);
                if (snapshot is { Active: true })
                {
                    return snapshot;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    private static IEnumerable<FileInfo> EnumeratePluginDataFilesSafe(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string[] files;
            try
            {
                files = Directory.GetFiles(
                    directory,
                    "LotroPresence.plugindata",
                    SearchOption.TopDirectoryOnly);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return new FileInfo(file);
            }

            string[] subdirectories;
            try
            {
                subdirectories = Directory.GetDirectories(directory);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var subdirectory in subdirectories)
            {
                try
                {
                    var attributes = File.GetAttributes(subdirectory);
                    if ((attributes & FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Push(subdirectory);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static bool IsFresh(FileInfo file, int timeoutSeconds)
    {
        var age = DateTime.UtcNow - file.LastWriteTimeUtc;
        return age >= TimeSpan.Zero &&
               age <= TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 40, 300));
    }

    private static PresenceSnapshot? TryReadSnapshot(string path)
    {
        try
        {
            string text;
            using (var stream = new FileStream(
                       path, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                text = reader.ReadToEnd();
            }

            var values = ParseFlatLuaTable(text);
            var schemaVersion = GetInt(values, "schemaVersion");
            if (schemaVersion != SupportedSchemaVersion)
            {
                if (ReportedSchemaWarnings.Add(path))
                {
                    Console.Error.WriteLine(
                        $"PluginData incompatible : schéma {schemaVersion}, attendu {SupportedSchemaVersion}.");
                }
                return null;
            }

            ReportedSchemaWarnings.Remove(path);
            var character = GetString(values, "character");
            if (string.IsNullOrWhiteSpace(character))
            {
                return null;
            }

            return new PresenceSnapshot(
                path,
                schemaVersion,
                character,
                GetInt(values, "level"),
                GetInt(values, "classId"),
                GetString(values, "className"),
                GetInt(values, "raceId"),
                GetString(values, "raceName"),
                Math.Max(1, GetInt(values, "partySize")),
                GetBool(values, "active"),
                GetServerNameFromCharacterPath(path, character));
        }
        catch
        {
            return null;
        }
    }

    private static string GetServerNameFromCharacterPath(string path, string character)
    {
        var characterDirectory = Directory.GetParent(path);
        if (characterDirectory is null ||
            !string.Equals(characterDirectory.Name, character, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var server = characterDirectory.Parent?.Name ?? string.Empty;
        return string.IsNullOrWhiteSpace(server) ||
               string.Equals(server, "AllServers", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(server, "AllCharacters", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : server;
    }

    private static Dictionary<string, string> ParseFlatLuaTable(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in EntryRegex.Matches(text))
        {
            result[match.Groups["key"].Value] = match.Groups["value"].Value;
        }
        return result;
    }

    private static RichPresence BuildPresence(
        PresenceSnapshot snapshot,
        BridgeConfig config,
        DateTime sessionStart) => new()
    {
        Details = Limit(BuildDetails(snapshot), 120),
        State = Limit(BuildState(snapshot), 120),
        Timestamps = new Timestamps { Start = sessionStart },
        Assets = BuildAssets(
            config,
            GetClassAssetKey(snapshot.ClassName),
            snapshot.ClassName)
    };

    private static RichPresence BuildSelectionPresence(
        BridgeConfig config,
        DateTime sessionStart) => new()
    {
        Details = "Sélection de personnage",
        Timestamps = new Timestamps { Start = sessionStart },
        Assets = BuildAssets(config, SelectionAssetKey, "Sélection de personnage")
    };

    private static Assets? BuildAssets(
        BridgeConfig config,
        string smallImageKey,
        string smallImageText)
    {
        var hasSmall = !string.IsNullOrWhiteSpace(smallImageKey);
        var hasLarge = !string.IsNullOrWhiteSpace(config.LargeImageKey);
        if (!hasSmall && !hasLarge)
        {
            return null;
        }

        var assets = new Assets();
        if (hasSmall)
        {
            assets.SmallImageKey = smallImageKey;
            assets.SmallImageText = string.IsNullOrWhiteSpace(smallImageText)
                ? "Classe LOTRO"
                : smallImageText;
        }

        if (hasLarge)
        {
            assets.LargeImageKey = config.LargeImageKey;
            assets.LargeImageText = string.IsNullOrWhiteSpace(config.LargeImageText)
                ? "The Lord of the Rings Online"
                : config.LargeImageText;
        }

        return assets;
    }

    private static string BuildDetails(PresenceSnapshot snapshot)
    {
        var parts = new List<string> { snapshot.Character };
        var duplicateBeorning =
            string.Equals(snapshot.ClassName, "Béornide", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(snapshot.RaceName, "Béornide", StringComparison.OrdinalIgnoreCase);

        if (!duplicateBeorning && !string.IsNullOrWhiteSpace(snapshot.RaceName))
        {
            parts.Add(snapshot.RaceName);
        }

        parts.Add(string.IsNullOrWhiteSpace(snapshot.ClassName)
            ? $"Niveau {snapshot.Level}"
            : $"{snapshot.ClassName} • Niveau {snapshot.Level}");
        return string.Join(" • ", parts);
    }

    private static string BuildState(PresenceSnapshot snapshot)
    {
        var parts = new List<string>
        {
            snapshot.PartySize > 1 ? $"Communauté de {snapshot.PartySize}" : "Solo"
        };
        if (!string.IsNullOrWhiteSpace(snapshot.ServerName))
        {
            parts.Add($"Serveur {snapshot.ServerName}");
        }
        return string.Join(" • ", parts);
    }

    private static string GetClassAssetKey(string className) =>
        ClassAssetKeys.TryGetValue(className, out var key) ? key : string.Empty;

    private static string BuildPresenceKey(PresenceSnapshot snapshot, DateTime sessionStart) =>
        string.Join('|',
            "character",
            sessionStart.Ticks,
            snapshot.FilePath,
            snapshot.SchemaVersion,
            snapshot.Character,
            snapshot.Level,
            snapshot.ClassId,
            snapshot.ClassName,
            snapshot.RaceId,
            snapshot.RaceName,
            snapshot.PartySize,
            snapshot.Active,
            snapshot.ServerName);

    private static string Limit(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength];

    private static string GetString(Dictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return string.Empty;
        }
        return value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? UnescapeLuaString(value[1..^1])
            : value;
    }

    private static int GetInt(Dictionary<string, string> values, string key) =>
        int.TryParse(GetString(values, key), out var value) ? value : 0;

    private static bool GetBool(Dictionary<string, string> values, string key) =>
        bool.TryParse(GetString(values, key), out var value) && value;

    private static string UnescapeLuaString(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '\\' || index + 1 >= value.Length)
            {
                builder.Append(value[index]);
                continue;
            }

            index++;
            builder.Append(value[index] switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '\\' => '\\',
                '"' => '"',
                _ => value[index]
            });
        }
        return builder.ToString();
    }

    private static int RunSelfTests()
    {
        var beorning = new PresenceSnapshot(
            "test", 4, "Heimvald", 28, 214, "Béornide",
            114, "Béornide", 1, true, "Orcrist");
        var champion = new PresenceSnapshot(
            "test", 4, "Altherian", 28, 172, "Champion",
            23, "Homme", 1, true, "Orcrist");
        var start = new DateTime(638000000000000000, DateTimeKind.Utc);
        var selection = BuildSelectionPresence(new BridgeConfig(), start);
        var mergedConfig = new BridgeConfig
        {
            DiscordApplicationId = "default-app",
            PollIntervalMilliseconds = 2000,
            DiscoveryIntervalSeconds = 10
        };
        using var overrides = JsonDocument.Parse(
            """{"PollIntervalMilliseconds":1500}""");
        ApplyConfigOverrides(mergedConfig, overrides.RootElement);

        var ok =
            BuildDetails(beorning) == "Heimvald • Béornide • Niveau 28" &&
            BuildState(beorning) == "Solo • Serveur Orcrist" &&
            GetClassAssetKey(beorning.ClassName) == "class_beorning" &&
            BuildDetails(champion) == "Altherian • Homme • Champion • Niveau 28" &&
            BuildState(champion) == "Solo • Serveur Orcrist" &&
            GetClassAssetKey(champion.ClassName) == "class_champion" &&
            selection.Details == "Sélection de personnage" &&
            string.IsNullOrWhiteSpace(selection.State) &&
            selection.Timestamps?.Start == start &&
            selection.Assets?.SmallImageKey == SelectionAssetKey &&
            mergedConfig.DiscordApplicationId == "default-app" &&
            mergedConfig.PollIntervalMilliseconds == 1500 &&
            mergedConfig.DiscoveryIntervalSeconds == 10;

        Console.WriteLine(ok ? "Self-test LotroPresence : OK" : "Self-test LotroPresence : ECHEC");
        return ok ? 0 : 1;
    }
}
