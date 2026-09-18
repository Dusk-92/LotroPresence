using System.Text;
using System.Text.RegularExpressions;

namespace LotroPresence.Bridge;

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

internal static class PluginDataReader
{
    internal const int SupportedSchemaVersion = 4;
    internal const int MaxPluginDataBytes = 64 * 1024;
    private static readonly TimeSpan FutureTimestampTolerance = TimeSpan.FromSeconds(10);

    private static readonly Regex EntryRegex = new(
        """\["(?<key>[^"]+)"\]\s*=\s*(?<value>"(?:\\.|[^"])*"|true|false|-?\d+(?:\.\d+)?)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> ReportedSchemaWarnings =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ReportedOversizeWarnings =
        new(StringComparer.OrdinalIgnoreCase);

    internal static PresenceSnapshot? ReadSelectedSnapshot(
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

    internal static PresenceSnapshot? FindLatestActiveSnapshot(string root, int timeoutSeconds)
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

    internal static IEnumerable<FileInfo> EnumeratePluginDataFilesSafe(string root)
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

    internal static bool IsFresh(FileInfo file, int timeoutSeconds) =>
        IsFresh(file.LastWriteTimeUtc, timeoutSeconds, DateTime.UtcNow);

    internal static bool IsFresh(DateTime lastWriteTimeUtc, int timeoutSeconds, DateTime nowUtc)
    {
        var age = nowUtc - lastWriteTimeUtc;
        return age >= -FutureTimestampTolerance &&
               age <= TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 40, 300));
    }

    internal static PresenceSnapshot? TryReadSnapshot(string path)
    {
        try
        {
            var file = new FileInfo(path);
            file.Refresh();
            if (!file.Exists)
            {
                return null;
            }

            if (file.Length > MaxPluginDataBytes)
            {
                WarnOversizeOnce(path);
                return null;
            }

            string text;
            using (var stream = new FileStream(
                       path, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                var buffer = new char[MaxPluginDataBytes + 1];
                var count = reader.ReadBlock(buffer, 0, buffer.Length);
                if (count > MaxPluginDataBytes)
                {
                    WarnOversizeOnce(path);
                    return null;
                }

                text = new string(buffer, 0, count);
            }

            ReportedOversizeWarnings.Remove(path);
            var values = ParseFlatLuaTable(text);
            var schemaVersion = GetInt(values, "schemaVersion");
            if (schemaVersion != SupportedSchemaVersion)
            {
                if (ReportedSchemaWarnings.Add(path))
                {
                    AppLog.Warning(
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
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    internal static string GetServerNameFromCharacterPath(string path, string character)
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

    internal static Dictionary<string, string> ParseFlatLuaTable(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in EntryRegex.Matches(text))
        {
            result[match.Groups["key"].Value] = match.Groups["value"].Value;
        }
        return result;
    }

    internal static string GetString(Dictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return string.Empty;
        }
        return value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? UnescapeLuaString(value[1..^1])
            : value;
    }

    internal static int GetInt(Dictionary<string, string> values, string key) =>
        int.TryParse(GetString(values, key), out var value) ? value : 0;

    internal static bool GetBool(Dictionary<string, string> values, string key) =>
        bool.TryParse(GetString(values, key), out var value) && value;

    internal static string UnescapeLuaString(string value)
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

    private static void WarnOversizeOnce(string path)
    {
        if (ReportedOversizeWarnings.Add(path))
        {
            AppLog.Warning(
                $"PluginData ignoré : taille supérieure à {MaxPluginDataBytes / 1024} Kio.");
        }
    }
}
