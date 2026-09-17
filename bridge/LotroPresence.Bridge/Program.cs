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
    public int HeartbeatTimeoutSeconds { get; set; } = 25;
    public int PollIntervalMilliseconds { get; set; } = 2000;
}

internal sealed record PresenceSnapshot(
    string FilePath,
    string Character,
    int Level,
    int ClassId,
    string ClassName,
    string ZoneName,
    bool Active,
    long Heartbeat,
    string ServerName);

internal static class Program
{
    private static readonly Regex EntryRegex = new(
        """\["(?<key>[^"]+)"\]\s*=\s*(?<value>"(?:\\.|[^"])*"|true|false|-?\d+(?:\.\d+)?)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static async Task<int> Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

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
            Console.Error.WriteLine("Discord Application ID manquant dans config.json.");
            return 2;
        }

        var pluginDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "The Lord of the Rings Online",
            "PluginData");

        using var discord = new DiscordRpcClient(applicationId);
        discord.Initialize();

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        Console.WriteLine("LotroPresence bridge démarré.");
        Console.WriteLine($"PluginData : {pluginDataRoot}");
        Console.WriteLine("Ctrl+C pour quitter.");

        string? selectedFile = null;
        string? lastPresenceKey = null;
        var presenceVisible = false;
        var sessionStart = DateTime.UtcNow;

        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var file = FindLatestPresenceFile(pluginDataRoot);
                PresenceSnapshot? snapshot = null;

                if (file is not null && IsFresh(file, config.HeartbeatTimeoutSeconds))
                {
                    snapshot = TryReadSnapshot(file.FullName);
                }

                if (snapshot is null || !snapshot.Active)
                {
                    if (presenceVisible)
                    {
                        discord.SetPresence(null);
                        presenceVisible = false;
                        lastPresenceKey = null;
                        selectedFile = null;
                        Console.WriteLine("Présence Discord effacée : LotroPresence n'est plus actif.");
                    }
                }
                else
                {
                    if (!string.Equals(selectedFile, snapshot.FilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedFile = snapshot.FilePath;
                        sessionStart = DateTime.UtcNow;
                        lastPresenceKey = null;
                    }

                    var presenceKey = BuildPresenceKey(snapshot);
                    if (!string.Equals(presenceKey, lastPresenceKey, StringComparison.Ordinal))
                    {
                        discord.SetPresence(BuildPresence(snapshot, config, sessionStart));
                        presenceVisible = true;
                        lastPresenceKey = presenceKey;

                        var place = string.Join(" • ", new[] { snapshot.ZoneName, snapshot.ServerName }
                            .Where(value => !string.IsNullOrWhiteSpace(value)));
                        Console.WriteLine($"Présence : {snapshot.Character} • {snapshot.ClassName} niveau {snapshot.Level}" +
                                          (string.IsNullOrWhiteSpace(place) ? string.Empty : $" | {place}"));
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
        }

        return 0;
    }

    private static BridgeConfig? LoadConfig()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"Fichier absent : {configPath}");
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
            Console.Error.WriteLine($"Impossible de lire config.json : {ex.Message}");
            return null;
        }
    }

    private static FileInfo? FindLatestPresenceFile(string pluginDataRoot)
    {
        if (!Directory.Exists(pluginDataRoot))
        {
            return null;
        }

        try
        {
            return Directory
                .EnumerateFiles(pluginDataRoot, "LotroPresence.plugindata", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsFresh(FileInfo file, int timeoutSeconds)
    {
        var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 15, 300));
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
            using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                text = reader.ReadToEnd();
            }

            var values = ParseFlatLuaTable(text);
            if (values.Count == 0)
            {
                return null;
            }

            var character = GetString(values, "character");
            if (string.IsNullOrWhiteSpace(character))
            {
                return null;
            }

            var serverName = GetServerNameFromCharacterPath(path, character);

            return new PresenceSnapshot(
                path,
                character,
                GetInt(values, "level"),
                GetInt(values, "classId"),
                GetString(values, "className"),
                GetString(values, "zoneName"),
                GetBool(values, "active"),
                GetLong(values, "heartbeat"),
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
        var directory = Directory.GetParent(path);

        while (directory is not null)
        {
            if (string.Equals(directory.Name, character, StringComparison.OrdinalIgnoreCase))
            {
                var server = directory.Parent?.Name ?? string.Empty;
                if (!string.Equals(server, "AllServers", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(server, "AllCharacters", StringComparison.OrdinalIgnoreCase))
                {
                    return server;
                }

                return string.Empty;
            }

            directory = directory.Parent;
        }

        // On ne devine jamais le serveur si le dossier du personnage n'est pas trouvé :
        // cela évite d'afficher accidentellement le nom du compte LOTRO.
        return string.Empty;
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

        var stateParts = new[] { snapshot.ZoneName, snapshot.ServerName }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        var presence = new RichPresence
        {
            Details = Limit($"{snapshot.Character} • {classText}", 120),
            State = Limit(string.Join(" • ", stateParts), 120),
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

    private static string BuildPresenceKey(PresenceSnapshot snapshot) => string.Join('|',
        snapshot.FilePath,
        snapshot.Character,
        snapshot.Level,
        snapshot.ClassId,
        snapshot.ClassName,
        snapshot.ZoneName,
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

    private static long GetLong(Dictionary<string, string> values, string key) =>
        long.TryParse(GetString(values, key), out var result) ? result : 0;

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
}
