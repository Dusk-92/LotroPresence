using System.Text;
using System.Text.Json;
using LotroPresence.Bridge;

namespace LotroPresence.Tests;

internal static class TestProgram
{
    private static int failures;

    public static int Main()
    {
        TestPresenceFormatting();
        TestAllClassAssets();
        TestSelectionPresence();
        TestConfigOverridesAndValidation();
        TestPluginDataParsingAndServerDetection();
        TestFreshnessTolerance();
        TestSteamArgumentPreservation();

        Console.WriteLine(failures == 0
            ? "LotroPresence.Tests : OK"
            : $"LotroPresence.Tests : {failures} échec(s)");
        return failures == 0 ? 0 : 1;
    }

    private static void TestPresenceFormatting()
    {
        var beorning = Snapshot(
            "Heimvald", 28, "Béornide", "Béornide", 1, "Orcrist");
        Equal(
            "Heimvald • Béornide • Niveau 28",
            PresenceFactory.BuildDetails(beorning),
            "format Béornide sans race dupliquée");
        Equal(
            "Solo • Serveur Orcrist",
            PresenceFactory.BuildState(beorning),
            "état solo");

        var champion = Snapshot(
            "Altherian", 28, "Champion", "Homme", 4, "Orcrist");
        Equal(
            "Altherian • Homme • Champion • Niveau 28",
            PresenceFactory.BuildDetails(champion),
            "format race/classe/niveau");
        Equal(
            "Communauté de 4 • Serveur Orcrist",
            PresenceFactory.BuildState(champion),
            "état communauté");
    }

    private static void TestAllClassAssets()
    {
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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

        foreach (var pair in expected)
        {
            Equal(pair.Value, PresenceFactory.GetClassAssetKey(pair.Key), $"asset {pair.Key}");
        }

        Equal(string.Empty, PresenceFactory.GetClassAssetKey("Classe inconnue"), "classe inconnue sans asset");
    }

    private static void TestSelectionPresence()
    {
        var start = new DateTime(638000000000000000, DateTimeKind.Utc);
        var presence = PresenceFactory.BuildSelectionPresence(new BridgeConfig(), start);
        Equal("Sélection de personnage", presence.Details, "texte sélection");
        Check(string.IsNullOrWhiteSpace(presence.State), "sélection sans seconde ligne dupliquée");
        Equal(PresenceFactory.SelectionAssetKey, presence.Assets?.SmallImageKey, "asset sélection");
        Equal(start, presence.Timestamps?.Start, "timer sélection");
    }

    private static void TestConfigOverridesAndValidation()
    {
        var config = new BridgeConfig
        {
            DiscordApplicationId = "1550150231092502613",
            PollIntervalMilliseconds = 2000,
            DiscoveryIntervalSeconds = 10
        };

        using (var document = JsonDocument.Parse(
                   """{"PollIntervalMilliseconds":1500,"FutureOption":true}"""))
        {
            var warnings = new List<string>();
            var errors = new List<string>();
            Check(
                BridgeConfigLoader.ApplyOverrides(config, document.RootElement, warnings, errors),
                "override config valide");
            Equal(1500, config.PollIntervalMilliseconds, "override partiel conservé");
            Equal(10, config.DiscoveryIntervalSeconds, "valeur par défaut conservée");
            Equal(1, warnings.Count, "clé inconnue signalée");
            Equal(0, errors.Count, "aucune erreur config valide");
        }

        using (var document = JsonDocument.Parse(
                   """{"PollIntervalMilliseconds":"vite"}"""))
        {
            var errors = new List<string>();
            Check(
                !BridgeConfigLoader.ApplyOverrides(config, document.RootElement, null, errors),
                "type config invalide refusé");
            Check(errors.Count == 1, "erreur de type signalée");
        }

        var invalid = new BridgeConfig
        {
            DiscordApplicationId = "1550150231092502613",
            HeartbeatTimeoutSeconds = 5,
            PollIntervalMilliseconds = 100,
            DiscoveryIntervalSeconds = 2,
            LargeImageKey = new string('a', 257),
            LargeImageText = new string('b', 129)
        };
        Check(BridgeConfigLoader.Validate(invalid).Count == 5, "bornes config validées");
        Check(
            BridgeConfigLoader.IsValidDiscordApplicationId("1550150231092502613"),
            "Application ID valide");
        Check(
            !BridgeConfigLoader.IsValidDiscordApplicationId("not-an-id"),
            "Application ID invalide refusé");
    }

    private static void TestPluginDataParsingAndServerDetection()
    {
        var root = Path.Combine(Path.GetTempPath(), "LotroPresenceTests", Guid.NewGuid().ToString("N"));
        var server = Path.Combine(root, "Orcrist");
        var activeCharacter = Path.Combine(server, "Altherian");
        var inactiveCharacter = Path.Combine(server, "Oldchar");
        Directory.CreateDirectory(activeCharacter);
        Directory.CreateDirectory(inactiveCharacter);

        try
        {
            var activePath = Path.Combine(activeCharacter, "LotroPresence.plugindata");
            File.WriteAllText(activePath, SnapshotText("Altherian", true), Encoding.UTF8);
            File.SetLastWriteTimeUtc(activePath, DateTime.UtcNow.AddSeconds(-2));

            var inactivePath = Path.Combine(inactiveCharacter, "LotroPresence.plugindata");
            File.WriteAllText(inactivePath, SnapshotText("Oldchar", false), Encoding.UTF8);
            File.SetLastWriteTimeUtc(inactivePath, DateTime.UtcNow);

            var parsed = PluginDataReader.TryReadSnapshot(activePath);
            Check(parsed is not null, "PluginData valide lu");
            Equal("Altherian", parsed?.Character, "nom PluginData");
            Equal("Orcrist", parsed?.ServerName, "serveur dérivé du chemin strict");
            Equal("Champion", parsed?.ClassName, "classe PluginData");
            Check(parsed?.Active == true, "flag active lu");

            var latest = PluginDataReader.FindLatestActiveSnapshot(root, 50);
            Equal("Altherian", latest?.Character, "snapshot inactive plus récent ignoré");

            var wrongPath = Path.Combine(server, "AutreDossier", "LotroPresence.plugindata");
            Directory.CreateDirectory(Path.GetDirectoryName(wrongPath)!);
            File.WriteAllText(wrongPath, SnapshotText("Mismatch", true), Encoding.UTF8);
            Equal(
                string.Empty,
                PluginDataReader.GetServerNameFromCharacterPath(wrongPath, "Mismatch"),
                "serveur non déduit si dossier personnage ne correspond pas");

            var hugePath = Path.Combine(activeCharacter, "huge.plugindata");
            File.WriteAllText(
                hugePath,
                new string('x', PluginDataReader.MaxPluginDataBytes + 1),
                Encoding.UTF8);
            Check(PluginDataReader.TryReadSnapshot(hugePath) is null, "PluginData surdimensionné refusé");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void TestFreshnessTolerance()
    {
        var now = DateTime.UtcNow;
        Check(PluginDataReader.IsFresh(now.AddSeconds(-49), 50, now), "snapshot frais accepté");
        Check(PluginDataReader.IsFresh(now.AddSeconds(5), 50, now), "petit décalage futur toléré");
        Check(!PluginDataReader.IsFresh(now.AddSeconds(20), 50, now), "grand décalage futur refusé");
        Check(!PluginDataReader.IsFresh(now.AddSeconds(-51), 50, now), "snapshot expiré refusé");
    }

    private static void TestSteamArgumentPreservation()
    {
        var command = new[]
        {
            @"C:\Games\LOTRO\LotroLauncher.exe",
            "-skiprawdownload",
            "--example-value",
            "Test Value",
            "--special-chars",
            "q[P>+x&!^"
        };
        var startInfo = SteamLaunchBridge.BuildStartInfo(command);
        Equal(command[0], startInfo.FileName, "Steam executable");
        Check(!startInfo.UseShellExecute, "Steam sans shell");
        Check(startInfo.ArgumentList.SequenceEqual(command.Skip(1)), "arguments Steam préservés");
    }

    private static PresenceSnapshot Snapshot(
        string character,
        int level,
        string className,
        string raceName,
        int partySize,
        string server) =>
        new("test", 4, character, level, 0, className, 0, raceName, partySize, true, server);

    private static string SnapshotText(string character, bool active) =>
        $$"""
        return {
            ["schemaVersion"] = 4,
            ["pluginVersion"] = "0.4.15",
            ["active"] = {{active.ToString().ToLowerInvariant()}},
            ["heartbeat"] = 123,
            ["character"] = "{{character}}",
            ["level"] = 28,
            ["classId"] = 172,
            ["className"] = "Champion",
            ["raceId"] = 23,
            ["raceName"] = "Homme",
            ["partySize"] = 1
        }
        """;

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (EqualityComparer<T>.Default.Equals(expected, actual))
        {
            return;
        }

        failures++;
        Console.Error.WriteLine($"ECHEC {name}: attendu={expected}, obtenu={actual}");
    }

    private static void Check(bool condition, string name)
    {
        if (condition)
        {
            return;
        }

        failures++;
        Console.Error.WriteLine($"ECHEC {name}");
    }
}
