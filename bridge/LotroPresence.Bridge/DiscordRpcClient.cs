using DiscordRPC;

namespace LotroPresence.Bridge;

/// <summary>
/// Petit adaptateur autour du client DiscordRPC.
/// La bibliothèque garde déjà une boucle de reconnexion IPC en arrière-plan ;
/// cet adaptateur rend simplement l'état réel visible dans la console et évite
/// de laisser croire que Discord est connecté quand il ne l'est pas encore.
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
                Console.WriteLine("Discord connecté. Présence synchronisée.");
            }
        };

        inner.OnConnectionFailed += (_, _) =>
        {
            if (Interlocked.Exchange(ref connectionState, 0) != 0)
            {
                Console.WriteLine("Discord indisponible. Reconnexion automatique en cours...");
            }
        };

        inner.OnClose += (_, message) =>
        {
            if (Interlocked.Exchange(ref connectionState, 0) != 0)
            {
                var reason = string.IsNullOrWhiteSpace(message?.Reason)
                    ? string.Empty
                    : $" ({message.Reason})";
                Console.WriteLine($"Connexion Discord perdue{reason}. Reconnexion automatique en cours...");
            }
        };

        inner.OnError += (_, message) =>
        {
            if (message is not null)
            {
                Console.Error.WriteLine($"Discord RPC : {message.Message}");
            }
        };
    }

    public bool Initialize()
    {
        var started = inner.Initialize();
        if (!started)
        {
            Console.Error.WriteLine("Impossible de démarrer la connexion Discord RPC.");
        }

        return started;
    }

    public void SetPresence(RichPresence? presence)
    {
        inner.SetPresence(presence);
    }

    public void Dispose()
    {
        inner.Dispose();
    }
}
