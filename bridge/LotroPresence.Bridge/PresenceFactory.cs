using DiscordRPC;

namespace LotroPresence.Bridge;

internal static class PresenceFactory
{
    internal const string SelectionAssetKey = "character_select";

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

    internal static RichPresence BuildPresence(
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

    internal static RichPresence BuildSelectionPresence(
        BridgeConfig config,
        DateTime sessionStart) => new()
    {
        Details = "Sélection de personnage",
        Timestamps = new Timestamps { Start = sessionStart },
        Assets = BuildAssets(config, SelectionAssetKey, "Sélection de personnage")
    };

    internal static Assets? BuildAssets(
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
                : Limit(smallImageText, 128);
        }

        if (hasLarge)
        {
            assets.LargeImageKey = config.LargeImageKey;
            assets.LargeImageText = string.IsNullOrWhiteSpace(config.LargeImageText)
                ? "The Lord of the Rings Online"
                : Limit(config.LargeImageText, 128);
        }

        return assets;
    }

    internal static string BuildDetails(PresenceSnapshot snapshot)
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

    internal static string BuildState(PresenceSnapshot snapshot)
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

    internal static string GetClassAssetKey(string className) =>
        ClassAssetKeys.TryGetValue(className, out var key) ? key : string.Empty;

    internal static string BuildPresenceKey(PresenceSnapshot snapshot, DateTime sessionStart) =>
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

    internal static string Limit(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength];
}
