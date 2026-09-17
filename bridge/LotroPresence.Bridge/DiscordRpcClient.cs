using DiscordRPC;

namespace LotroPresence.Bridge;

/// <summary>
/// Petit adaptateur autour du client DiscordRPC.
/// Les erreurs IPC sont journalisées localement sans faire tomber le bridge.
/// </summary>
internal sealed class DiscordRpcClient : IDisposable
{
    private readonly DiscordRPC.DiscordRpcClient inner;
    private int connectionState = -1;

    public DiscordRpcClient(string applicationId)
    {
        inner = new DiscordRPC.DiscordRpcClient(applicationId);

        inner.OnReady += (_, _) =>
        {
            if (Interlocked.Exchange(ref connectionState, 1) != 1)
            {
                AppLog.Info("Discord connecté. Présence synchronisée.");
            }
        };

        inner.OnConnectionFailed += (_, _) =>
        {
            if (Interlocked.Exchange(ref connectionState, 0) != 0)
            {
                AppLog.Warning("Discord indisponible. Reconnexion automatique en cours.");
            }
        };

        inner.OnClose += (_, message) =>
        {
            if (Interlocked.Exchange(ref connectionState, 0) != 0)
            {
                var reason = string.IsNullOrWhiteSpace(message?.Reason)
                    ? string.Empty
                    : $" ({message.Reason})";
                AppLog.Warning($"Connexion Discord perdue{reason}. Reconnexion automatique en cours.");
            }
        };

        inner.OnError += (_, message) =>
        {
            if (message is not null)
            {
                AppLog.Error($"Discord RPC : {message.Message}");
            }
        };
    }

    public bool Initialize()
    {
        try
        {
            var started = inner.Initialize();
            if (!started)
            {
                AppLog.Error("Impossible de démarrer la connexion Discord RPC.");
            }
            return started;
        }
        catch (Exception exception)
        {
            AppLog.Error("Exception pendant l'initialisation Discord RPC.", exception);
            return false;
        }
    }

    public bool SetPresence(RichPresence? presence)
    {
        try
        {
            inner.SetPresence(presence);
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error("Impossible de mettre à jour la présence Discord.", exception);
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            inner.Dispose();
        }
        catch (Exception exception)
        {
            AppLog.Error("Erreur pendant la fermeture de Discord RPC.", exception);
        }
    }
}
