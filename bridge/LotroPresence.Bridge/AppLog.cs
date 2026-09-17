namespace LotroPresence.Bridge;

internal static class AppLog
{
    private const long MaxLogBytes = 512 * 1024;
    private const int RotatedFileCount = 3;
    private static readonly object Sync = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LotroPresence");
    private static readonly string LogPath = Path.Combine(LogDirectory, "LotroPresence.log");

    internal static string CurrentLogPath => LogPath;

    internal static void Info(string message) => Write("INFO", message, null);

    internal static void Warning(string message) => Write("WARN", message, null);

    internal static void Error(string message, Exception? exception = null) =>
        Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                RotateIfNeeded();

                var cleanMessage = OneLine(message);
                var suffix = exception is null
                    ? string.Empty
                    : $" | {exception.GetType().Name}: {OneLine(exception.Message)}";
                var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {cleanMessage}{suffix}{Environment.NewLine}";
                File.AppendAllText(LogPath, line, System.Text.Encoding.UTF8);
            }
        }
        catch
        {
            // Le diagnostic ne doit jamais casser le bridge.
        }
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(LogPath) || new FileInfo(LogPath).Length < MaxLogBytes)
        {
            return;
        }

        for (var index = RotatedFileCount; index >= 2; index--)
        {
            var source = $"{LogPath}.{index - 1}";
            var destination = $"{LogPath}.{index}";
            if (File.Exists(source))
            {
                File.Move(source, destination, overwrite: true);
            }
        }

        File.Move(LogPath, $"{LogPath}.1", overwrite: true);
    }

    private static string OneLine(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
