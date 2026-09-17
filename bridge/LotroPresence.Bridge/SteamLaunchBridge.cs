using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace LotroPresence.Bridge;

/// <summary>
/// Permet à Steam de lancer LotroPresence comme wrapper sans passer par cmd.exe.
/// Tout ce qui suit --steam-launch est relancé comme commande LOTRO en conservant
/// chaque argument séparément, y compris les caractères spéciaux.
/// </summary>
internal static class SteamLaunchBridge
{
    private const string SteamLaunchSwitch = "--steam-launch";

    [ModuleInitializer]
    internal static void Initialize()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();

        if (args.Any(arg =>
                string.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            if (!RunSelfTests())
            {
                Environment.Exit(1);
            }

            return;
        }

        if (args.Length == 0 ||
            !string.Equals(args[0], SteamLaunchSwitch, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var command = args.Skip(1).ToArray();

        try
        {
            using var process = Process.Start(BuildStartInfo(command));
            if (process is null)
            {
                Environment.Exit(2);
            }
        }
        catch
        {
            // WinExe n'a volontairement pas de console. En cas d'échec du lancement
            // de LOTRO, on quitte avec un code d'erreur au lieu de laisser un bridge
            // invisible tourner sans avoir démarré le jeu.
            Environment.Exit(2);
        }
    }

    internal static ProcessStartInfo BuildStartInfo(IReadOnlyList<string> command)
    {
        if (command.Count == 0 || string.IsNullOrWhiteSpace(command[0]))
        {
            throw new ArgumentException(
                "--steam-launch doit être suivi de la commande LOTRO.",
                nameof(command));
        }

        var executable = command[0];
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false
        };

        var workingDirectory = Path.GetDirectoryName(executable);
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        for (var index = 1; index < command.Count; index++)
        {
            startInfo.ArgumentList.Add(command[index]);
        }

        return startInfo;
    }

    private static bool RunSelfTests()
    {
        try
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

            var startInfo = BuildStartInfo(command);

            if (!string.Equals(
                    startInfo.FileName,
                    command[0],
                    StringComparison.Ordinal) ||
                startInfo.UseShellExecute ||
                !string.Equals(
                    startInfo.WorkingDirectory,
                    @"C:\Games\LOTRO",
                    StringComparison.OrdinalIgnoreCase) ||
                !startInfo.ArgumentList.SequenceEqual(command.Skip(1)))
            {
                return false;
            }

            try
            {
                BuildStartInfo(Array.Empty<string>());
                return false;
            }
            catch (ArgumentException)
            {
                return true;
            }
        }
        catch
        {
            return false;
        }
    }
}
