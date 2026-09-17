using System.Text;
using System.Text.Json;

namespace LotroPresence.Bridge;

internal sealed class BridgeConfig
{
    public string DiscordApplicationId { get; set; } = string.Empty;
    public string LargeImageKey { get; set; } = string.Empty;
    public string LargeImageText { get; set; } = "The Lord of the Rings Online";
    public int HeartbeatTimeoutSeconds { get; set; } = 50;
    public int PollIntervalMilliseconds { get; set; } = 2000;
    public int DiscoveryIntervalSeconds { get; set; } = 10;
}

internal static class BridgeConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal static BridgeConfig? Load()
    {
        var localPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        var defaultPath = Path.Combine(AppContext.BaseDirectory, "config.default.json");

        try
        {
            if (!File.Exists(defaultPath))
            {
                AppLog.Error("Configuration par défaut introuvable.");
                return null;
            }

            var config = JsonSerializer.Deserialize<BridgeConfig>(
                File.ReadAllText(defaultPath, Encoding.UTF8),
                JsonOptions);
            if (config is null)
            {
                AppLog.Error("Configuration par défaut illisible.");
                return null;
            }

            NormalizeStrings(config);

            if (File.Exists(localPath))
            {
                using var localDocument = JsonDocument.Parse(
                    File.ReadAllText(localPath, Encoding.UTF8));
                if (localDocument.RootElement.ValueKind != JsonValueKind.Object)
                {
                    AppLog.Error("config.json doit contenir un objet JSON.");
                    return null;
                }

                var warnings = new List<string>();
                var errors = new List<string>();
                if (!ApplyOverrides(config, localDocument.RootElement, warnings, errors))
                {
                    foreach (var warning in warnings)
                    {
                        AppLog.Warning(warning);
                    }
                    foreach (var error in errors)
                    {
                        AppLog.Error(error);
                    }
                    return null;
                }

                foreach (var warning in warnings)
                {
                    AppLog.Warning(warning);
                }
            }

            NormalizeStrings(config);
            var validationErrors = Validate(config);
            if (validationErrors.Count > 0)
            {
                foreach (var error in validationErrors)
                {
                    AppLog.Error(error);
                }
                return null;
            }

            return config;
        }
        catch (JsonException exception)
        {
            AppLog.Error("Configuration JSON invalide.", exception);
            return null;
        }
        catch (IOException exception)
        {
            AppLog.Error("Impossible de lire la configuration.", exception);
            return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            AppLog.Error("Accès refusé pendant la lecture de la configuration.", exception);
            return null;
        }
    }

    internal static bool ApplyOverrides(
        BridgeConfig config,
        JsonElement root,
        ICollection<string>? warnings = null,
        ICollection<string>? errors = null)
    {
        warnings ??= new List<string>();
        errors ??= new List<string>();

        foreach (var property in root.EnumerateObject())
        {
            var name = property.Name;
            if (name.Equals("DiscordApplicationId", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryGetString(property, out var value, errors))
                {
                    continue;
                }
                config.DiscordApplicationId = value;
            }
            else if (name.Equals("LargeImageKey", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryGetString(property, out var value, errors))
                {
                    continue;
                }
                config.LargeImageKey = value;
            }
            else if (name.Equals("LargeImageText", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryGetString(property, out var value, errors))
                {
                    continue;
                }
                config.LargeImageText = value;
            }
            else if (name.Equals("HeartbeatTimeoutSeconds", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryGetInt32(property, out var value, errors))
                {
                    continue;
                }
                config.HeartbeatTimeoutSeconds = value;
            }
            else if (name.Equals("PollIntervalMilliseconds", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryGetInt32(property, out var value, errors))
                {
                    continue;
                }
                config.PollIntervalMilliseconds = value;
            }
            else if (name.Equals("DiscoveryIntervalSeconds", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryGetInt32(property, out var value, errors))
                {
                    continue;
                }
                config.DiscoveryIntervalSeconds = value;
            }
            else
            {
                warnings.Add($"Clé config.json inconnue ignorée : {name}.");
            }
        }

        return errors.Count == 0;
    }

    internal static List<string> Validate(BridgeConfig config)
    {
        NormalizeStrings(config);
        var errors = new List<string>();

        if (config.LargeImageKey.Length > 256)
        {
            errors.Add("LargeImageKey dépasse 256 caractères.");
        }
        if (config.LargeImageText.Length > 128)
        {
            errors.Add("LargeImageText dépasse 128 caractères.");
        }
        if (config.HeartbeatTimeoutSeconds is < 40 or > 300)
        {
            errors.Add("HeartbeatTimeoutSeconds doit être compris entre 40 et 300.");
        }
        if (config.PollIntervalMilliseconds is < 500 or > 30000)
        {
            errors.Add("PollIntervalMilliseconds doit être compris entre 500 et 30000.");
        }
        if (config.DiscoveryIntervalSeconds is < 5 or > 120)
        {
            errors.Add("DiscoveryIntervalSeconds doit être compris entre 5 et 120.");
        }

        return errors;
    }

    internal static bool IsValidDiscordApplicationId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length is >= 17 and <= 20 &&
        ulong.TryParse(value, out _);

    private static void NormalizeStrings(BridgeConfig config)
    {
        config.DiscordApplicationId ??= string.Empty;
        config.LargeImageKey ??= string.Empty;
        config.LargeImageText ??= string.Empty;
    }

    private static bool TryGetString(
        JsonProperty property,
        out string value,
        ICollection<string> errors)
    {
        if (property.Value.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{property.Name} doit être une chaîne de caractères.");
            value = string.Empty;
            return false;
        }

        value = property.Value.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryGetInt32(
        JsonProperty property,
        out int value,
        ICollection<string> errors)
    {
        if (property.Value.ValueKind != JsonValueKind.Number ||
            !property.Value.TryGetInt32(out value))
        {
            errors.Add($"{property.Name} doit être un entier.");
            value = 0;
            return false;
        }

        return true;
    }
}
