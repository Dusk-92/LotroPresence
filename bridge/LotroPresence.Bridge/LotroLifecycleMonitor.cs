using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace LotroPresence.Bridge;

/// <summary>
/// Surveille uniquement l'existence du processus LOTRO pour fermer le bridge
/// automatiquement lorsque le client de jeu est réellement quitté.
/// Aucun accès à la mémoire du jeu n'est effectué.
/// </summary>
internal static class LotroLifecycleMonitor
{
    private static readonly string[] LotroProcessNames =
    {
        "lotroclient64",
        "lotroclient"
    };

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(10);

    [ModuleInitializer]
    internal static void Start()
    {
        if (Environment.GetCommandLineArgs().Any(arg =>
                string.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var thread = new Thread(MonitorLoop)
        {
            IsBackground = true,
            Name = "LOTRO lifecycle monitor"
        };

        thread.Start();
    }

    private static void MonitorLoop()
    {
        var lotroSeen = false;
        DateTime? missingSinceUtc = null;

        while (true)
        {
            var running = TryIsLotroRunning();
            if (running is true)
            {
                lotroSeen = true;
                missingSinceUtc = null;
            }
            else if (running is false && lotroSeen)
            {
                missingSinceUtc ??= DateTime.UtcNow;

                if (DateTime.UtcNow - missingSinceUtc.Value >= ExitGrace)
                {
                    Environment.Exit(0);
                    return;
                }
            }

            Thread.Sleep(PollInterval);
        }
    }

    private static bool? TryIsLotroRunning()
    {
        try
        {
            foreach (var processName in LotroProcessNames)
            {
                var processes = Process.GetProcessesByName(processName);
                try
                {
                    if (processes.Length > 0)
                    {
                        return true;
                    }
                }
                finally
                {
                    foreach (var process in processes)
                    {
                        process.Dispose();
                    }
                }
            }

            return false;
        }
        catch
        {
            // En cas d'échec ponctuel de l'énumération des processus, on ne
            // provoque jamais une fermeture erronée du bridge.
            return null;
        }
    }
}
