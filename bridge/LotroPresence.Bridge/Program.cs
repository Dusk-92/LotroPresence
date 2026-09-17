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

    private static readonly Regex EntryRegex = new(
        """\["(?<key>[^"]+)"\]\s*=\s*(?<value>"(?:\\.|[^"])*"|true|false|-?\d+(?:\.\d+)?)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> ReportedSchemaWarnings =
        new(StringComparer.OrdinalIgnoreCase);

    public static async Task<int> Main(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            return RunSelfTests();
        }

        using var singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: MutexName,
            createdNew: out var createdNew);

        if (!createdNew)
        {
            Console.WriteLine("LotroPresence est déjà lancé.");
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
            Console.Error.WriteLine("Discord Application ID manquant dans la configuration.");
            return 2;
        }

        var pluginDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "The Lord of the Rings Online",
            "PluginData");

        using var discord = new DiscordRpcClient(applicationId);
        discord.Initialize();

        using var cancellation = new CancellationTokenSource();

        Console.WriteLine("LotroPresence bridge démarré.");
        Console.WriteLine($"PluginData : {pluginDataRoot}");
        Console.WriteLine("Ctrl+C pour quitter.");

        FileInfo? selectedFile = null;
        string? sessionFilePath = null;
        string? lastPresenceKey = null;
        var presenceVisible = false;
        var sessionStart = DateTime.UtcNow;
        var nextDiscoveryUtc = DateTime.MinValue;

        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var nowUtc = DateTime.UtcNow;
                PresenceSnapshot? snapshot = null;

                if (selectedFile is not null)
                {
                    try
                    {
                        selectedFile.Refresh();
                        if (!selectedFile.Exists ||
                            !IsFresh(selectedFile, config.HeartbeatTimeoutSeconds))
                        {
                            selectedFile = null;
                            nextDiscoveryUtc = DateTime.MinValue;
                        }
                        else
                        {
                            snapshot = TryReadSnapshot(selectedFile.FullName);
                            if (snapshot is not { Active: true })
                            {
                                selectedFile = null;
                                snapshot = null;
                                nextDiscoveryUtc = DateTime.MinValue;
                            }
                        }
                    }
                    catch (IOException)
                    {
                        selectedFile = null;
                        nextDiscoveryUtc = DateTime.MinValue;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        selectedFile = null;
                        nextDiscoveryUtc = DateTime.MinValue;
                    }
                }

                // Même avec un personnage actif, on refait périodiquement une
                // découverte légère afin de détecter un changement de personnage
                // sans attendre l'expiration de l'ancien heartbeat.
                if (nowUtc >= nextDiscoveryUtc)
                {
                    var discovered = FindLatestActiveSnapshot(
                        pluginDataRoot,
                        config.HeartbeatTimeoutSeconds);

                    nextDiscoveryUtc = nowUtc.AddSeconds(
                        Math.Clamp(config.DiscoveryIntervalSeconds, 5, 120));

                    if (discovered is not null)
                    {
                        selectedFile = new FileInfo(discovered.FilePath);
                        snapshot = discovered;
                    }
                }

                if (snapshot is null || !snapshot.Active)
                {
                    if (presenceVisible)
                    {
                        discord.SetPresence(null);
                        presenceVisible = false;
                        lastPresenceKey = null;
                        sessionFilePath = null;
                        Console.WriteLine("Présence Discord effacée : LotroPresence n'est plus actif.");
                    }
                }
                else
                {
                    if (!string.Equals(
                            sessionFilePath,
                            snapshot.FilePath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        sessionFilePath = snapshot.FilePath;
                        sessionStart = DateTime.UtcNow;
                        lastPresenceKey = null;
                    }

                    var presenceKey = BuildPresenceKey(snapshot);
                    if (!string.Equals(presenceKey, lastPresenceKey, StringComparison.Ordinal))
                    {
                        discord.SetPresence(BuildPresence(snapshot, config, sessionStart));
                        presenceVisible = true;
                        lastPresenceKey = presenceKey;

                        Console.WriteLine(
                            $"Présence : {snapshot.Character} • {snapshot.ClassName} niveau {snapshot.Level} | {BuildState(snapshot)}");
                    }
                }

                await Task.Delay(
                    Math.Clamp(config.PollIntervalMilliseconds, 500, 30000),
                    cancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Arrêt normal via Ctrl+C.
        }
        finally
        {
            discord.SetPresence(null);
            GC.KeepAlive(singleInstanceMutex);
        }

        return 0;
    }

    private static BridgeConfig? LoadConfig()
    {
        var localConfigPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        var defaultConfigPath = Path.Combine(AppContext.BaseDirectory, "config.default.json");

        var configPath = File.Exists(localConfigPath)
            ? localConfigPath
            : defaultConfigPath;

        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine("Configuration absente : config.json ou config.default.json.");
            return null;
        }

        try
        {
            var json = File.ReadAllText(configPath, Encoding.UTF8);
            return JsonSerializer.Deserialize<BridgeConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Impossible de lire {Path.GetFileName(configPath)} : {ex.Message}");
            return null;
        }
    }

    private static PresenceSnapshot? FindLatestActiveSnapshot(
        string pluginDataRoot,
        int timeoutSeconds)
    {
        if (!Directory.Exists(pluginDataRoot))
        {
            return null;
        }

        try
        {
            var files = Directory
                .EnumerateFiles(
                    pluginDataRoot,
                    "LotroPresence.plugindata",
                    SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .Where(file => IsFresh(file, timeoutSeconds))
                .OrderByDescending(file => file.LastWriteTimeUtc);

            foreach (var file in files)
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
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    private static bool IsFresh(FileInfo file, int timeoutSeconds)
    {
        var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 40, 300));
        return DateTime.UtcNow - file.LastWriteTimeUtc <= timeout;
    }

    private static PresenceSnapshot? TryReadSnapshot(string path)
    {
        try
        {
            string text;
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(
                       stream,
                       Encoding.UTF8,
                       detectEncodingFromByteOrderMarks: true))
            {
                text = reader.ReadToEnd();
            }

            var values = ParseFlatLuaTable(text);
            if (values.Count == 0)
            {
                return null;
            }

            var schemaVersion = GetInt(values, "schemaVersion");
            if (schemaVersion != SupportedSchemaVersion)
            {
                if (ReportedSchemaWarnings.Add(path))
                {
                    Console.Error.WriteLine(
                        $"PluginData incompatible : schéma {schemaVersion}, attendu {SupportedSchemaVersion}. " +
                        "Mets à jour le plugin LOTRO et le bridge ensemble.");
                }

                return null;
            }

            ReportedSchemaWarnings.Remove(path);

            var character = GetString(values, "character");
            if (string.IsNullOrWhiteSpace(character))
            {
                return null;
            }

            var serverName = GetServerNameFromCharacterPath(path, character);

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
                serverName);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Erreur de lecture PluginData : {ex.Message}");
            return null;
        }
    }

    private static string GetServerNameFromCharacterPath(string path, string character)
    {
        var characterDirectory = Directory.GetParent(path);
        if (characterDirectory is null ||
            !string.Equals(
                characterDirectory.Name,
                character,
                StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var serverDirectory = characterDirectory.Parent;
        if (serverDirectory is null)
        {
            return string.Empty;
        }

        var server = serverDirectory.Name;
        if (string.IsNullOrWhiteSpace(server) ||
            string.Equals(server, "AllServers", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(server, "AllCharacters", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return server;
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
        DateTime sessionStart)
    {
        var classText = string.IsNullOrWhiteSpace(snapshot.ClassName)
            ? $"Niveau {snapshot.Level}"
            : $"{snapshot.ClassName} niveau {snapshot.Level}";

        var presence = new RichPresence
        {
            Details = Limit($"{snapshot.Character} • {classText}", 120),
            State = Limit(BuildState(snapshot), 120),
            Timestamps = new Timestamps
            {
                Start = sessionStart
            }
        };

        if (!string.IsNullOrWhiteSpace(config.LargeImageKey))
        {
            presence.Assets = new Assets
            {
                LargeImageKey = config.LargeImageKey,
                LargeImageText = string.IsNullOrWhiteSpace(config.LargeImageText)
                    ? "The Lord of the Rings Online"
                    : config.LargeImageText
            };
        }

        return presence;
    }

    private static string BuildState(PresenceSnapshot snapshot)
    {
        var parts = new List<string>();

        var duplicateBeorning =
            string.Equals(snapshot.ClassName, "Béornide", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(snapshot.RaceName, "Béornide", StringComparison.OrdinalIgnoreCase);

        if (!duplicateBeorning && !string.IsNullOrWhiteSpace(snapshot.RaceName))
        {
            parts.Add(snapshot.RaceName);
        }

        parts.Add(snapshot.PartySize > 1
            ? $"Communauté de {snapshot.PartySize}"
            : "Solo");

        if (!string.IsNullOrWhiteSpace(snapshot.ServerName))
        {
            parts.Add($"Serveur {snapshot.ServerName}");
        }

        return string.Join(" • ", parts);
    }

    private static string BuildPresenceKey(PresenceSnapshot snapshot) => string.Join('|',
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

        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return UnescapeLuaString(value[1..^1]);
        }

        return value;
    }

    private static int GetInt(Dictionary<string, string> values, string key) =>
        int.TryParse(GetString(values, key), out var result) ? result : 0;

    private static bool GetBool(Dictionary<string, string> values, string key) =>
        bool.TryParse(GetString(values, key), out var result) && result;

    private static string UnescapeLuaString(string value)
    {
        var builder = new StringBuilder(value.Length);

        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length)
            {
                builder.Append(value[i]);
                continue;
            }

            i++;
            builder.Append(value[i] switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '\\' => '\\',
                '"' => '"',
                _ => value[i]
            });
        }

        return builder.ToString();
    }

    private static int RunSelfTests()
    {
        var failures = new List<string>();

        static void Check(bool condition, string name, List<string> errors)
        {
            if (!condition)
            {
                errors.Add(name);
            }
        }

        var beorning = new PresenceSnapshot(
            "test", SupportedSchemaVersion, "Heimvald", 28, 214, "Béornide",
            114, "Béornide", 1, true, "Orcrist");

        Check(
            BuildState(beorning) == "Solo • Serveur Orcrist",
            "Règle spéciale Béornide",
            failures);

        var champion = new PresenceSnapshot(
            "test", SupportedSchemaVersion, "Anarmir", 67, 0, "Champion",
            23, "Homme", 4, true, "Orcrist");

        Check(
            BuildState(champion) == "Homme • Communauté de 4 • Serveur Orcrist",
            "Race + communauté + serveur",
            failures);

        var parsed = ParseFlatLuaTable(
            """return { ["schemaVersion"] = 4, ["character"] = "Heimvald", ["active"] = true, ["level"] = 28 }""");

        Check(
            GetInt(parsed, "schemaVersion") == SupportedSchemaVersion,
            "Parsing schemaVersion",
            failures);
        Check(
            GetString(parsed, "character") == "Heimvald",
            "Parsing personnage",
            failures);
        Check(
            GetBool(parsed, "active"),
            "Parsing booléen",
            failures);

        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "LotroPresenceSelfTest-" + Guid.NewGuid().ToString("N"));

        try
        {
            var accountDirectory = Path.Combine(tempRoot, "ComptePrive");
            var heimvaldDirectory = Path.Combine(accountDirectory, "Orcrist", "Heimvald");
            Directory.CreateDirectory(heimvaldDirectory);
            var heimvaldPath = Path.Combine(
                heimvaldDirectory,
                "LotroPresence.plugindata");

            File.WriteAllText(
                heimvaldPath,
                """return { ["schemaVersion"] = 4, ["character"] = "Heimvald", ["level"] = 28, ["classId"] = 214, ["className"] = "Béornide", ["raceId"] = 114, ["raceName"] = "Béornide", ["partySize"] = 1, ["active"] = true, ["heartbeat"] = 0 }""",
                Encoding.UTF8);

            var snapshot = TryReadSnapshot(heimvaldPath);
            Check(snapshot is not null, "Lecture snapshot valide", failures);
            Check(
                snapshot?.ServerName == "Orcrist",
                "Détection serveur stricte",
                failures);
            Check(
                GetServerNameFromCharacterPath(
                    heimvaldPath,
                    "AutrePersonnage") == string.Empty,
                "Protection nom de compte",
                failures);

            var nestedDirectory = Path.Combine(heimvaldDirectory, "SousDossier");
            Directory.CreateDirectory(nestedDirectory);
            var nestedPath = Path.Combine(
                nestedDirectory,
                "LotroPresence.plugindata");
            Check(
                GetServerNameFromCharacterPath(
                    nestedPath,
                    "Heimvald") == string.Empty,
                "Refus serveur si le fichier n'est pas directement sous le personnage",
                failures);

            // Un fichier inactif plus récent ne doit pas masquer un personnage actif.
            var inactiveDirectory = Path.Combine(accountDirectory, "Orcrist", "AncienPerso");
            Directory.CreateDirectory(inactiveDirectory);
            var inactivePath = Path.Combine(
                inactiveDirectory,
                "LotroPresence.plugindata");

            File.WriteAllText(
                inactivePath,
                """return { ["schemaVersion"] = 4, ["character"] = "AncienPerso", ["level"] = 20, ["classId"] = 0, ["className"] = "Champion", ["raceId"] = 0, ["raceName"] = "Homme", ["partySize"] = 1, ["active"] = false, ["heartbeat"] = 0 }""",
                Encoding.UTF8);

            File.SetLastWriteTimeUtc(heimvaldPath, DateTime.UtcNow.AddSeconds(-3));
            File.SetLastWriteTimeUtc(inactivePath, DateTime.UtcNow.AddSeconds(-2));

            var activeDespiteNewerInactive =
                FindLatestActiveSnapshot(tempRoot, 50);
            Check(
                activeDespiteNewerInactive?.Character == "Heimvald",
                "Ignorer un PluginData inactif plus récent",
                failures);

            // Un nouveau personnage actif doit remplacer l'ancien sans attendre
            // le timeout de son fichier.
            var anarmirDirectory = Path.Combine(accountDirectory, "Orcrist", "Anarmir");
            Directory.CreateDirectory(anarmirDirectory);
            var anarmirPath = Path.Combine(
                anarmirDirectory,
                "LotroPresence.plugindata");

            File.WriteAllText(
                anarmirPath,
                """return { ["schemaVersion"] = 4, ["character"] = "Anarmir", ["level"] = 67, ["classId"] = 0, ["className"] = "Champion", ["raceId"] = 23, ["raceName"] = "Homme", ["partySize"] = 4, ["active"] = true, ["heartbeat"] = 0 }""",
                Encoding.UTF8);

            File.SetLastWriteTimeUtc(anarmirPath, DateTime.UtcNow.AddSeconds(-1));

            var newestActive = FindLatestActiveSnapshot(tempRoot, 50);
            Check(
                newestActive?.Character == "Anarmir",
                "Sélection du personnage actif le plus récent",
                failures);

            File.WriteAllText(
                heimvaldPath,
                """return { ["schemaVersion"] = 3, ["character"] = "Heimvald", ["active"] = true }""",
                Encoding.UTF8);

            Check(
                TryReadSnapshot(heimvaldPath) is null,
                "Refus d'un schéma incompatible",
                failures);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch
            {
                // Le nettoyage ne doit pas faire échouer les tests.
            }
        }

        if (failures.Count == 0)
        {
            Console.WriteLine("Self-test LotroPresence : OK");
            return 0;
        }

        Console.Error.WriteLine("Self-test LotroPresence : ECHEC");
        foreach (var failure in failures)
        {
            Console.Error.WriteLine($"- {failure}");
        }

        return 1;
    }
}
