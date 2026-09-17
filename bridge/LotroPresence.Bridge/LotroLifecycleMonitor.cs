using System.Diagnostics;

namespace LotroPresence.Bridge;

/// <summary>
/// Surveille uniquement l'existence du processus LOTRO dans la session Windows
/// courante. Aucun accès à la mémoire du jeu n'est effectué.
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
    private static long sessionStartedUtcTicks;
    private static int exitRequested;
    private static int lotroRunning;
    private static int started;
    private static int currentWindowsSessionId;

    internal static DateTime? SessionStartedUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref sessionStartedUtcTicks);
            return ticks == 0
                ? null
                : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    internal static bool ExitRequested => Volatile.Read(ref exitRequested) != 0;

    internal static bool IsLotroRunning => Volatile.Read(ref lotroRunning) != 0;

    internal static void Start()
    {
        if (Interlocked.Exchange(ref started, 1) != 0)
        {
            return;
        }

        using (var current = Process.GetCurrentProcess())
        {
            currentWindowsSessionId = current.SessionId;
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
                Volatile.Write(ref lotroRunning, 1);

                if (!lotroSeen)
                {
                    var detectedUtc = DateTime.UtcNow;
                    Interlocked.CompareExchange(
                        ref sessionStartedUtcTicks,
                        detectedUtc.Ticks,
                        comparand: 0);
                    AppLog.Info("Client LOTRO détecté dans la session Windows courante.");
                }
                else if (missingSinceUtc is not null)
                {
                    AppLog.Info("Client LOTRO de nouveau détecté pendant la période de grâce.");
                }

                lotroSeen = true;
                missingSinceUtc = null;
            }
            else if (running is false)
            {
                Volatile.Write(ref lotroRunning, 0);

                if (lotroSeen)
                {
                    if (missingSinceUtc is null)
                    {
                        missingSinceUtc = DateTime.UtcNow;
                        AppLog.Info("Client LOTRO absent : présence Discord masquée pendant la période de grâce.");
                    }

                    if (DateTime.UtcNow - missingSinceUtc.Value >= ExitGrace)
                    {
                        Volatile.Write(ref exitRequested, 1);
                        AppLog.Info("Client LOTRO absent depuis 10 secondes : fermeture demandée.");
                        return;
                    }
                }
            }

            Thread.Sleep(PollInterval);
        }
    }

    private static bool? TryIsLotroRunning()
    {
        var uncertain = false;

        try
        {
            foreach (var processName in LotroProcessNames)
            {
                var processes = Process.GetProcessesByName(processName);
                try
                {
                    foreach (var process in processes)
                    {
                        try
                        {
                            if (process.SessionId == currentWindowsSessionId)
                            {
                                return true;
                            }
                        }
                        catch
                        {
                            uncertain = true;
                        }
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

            return uncertain ? null : false;
        }
        catch
        {
            // Une erreur ponctuelle d'énumération ne doit pas provoquer une
            // fermeture erronée ni effacer une présence valide.
            return null;
        }
    }
}
