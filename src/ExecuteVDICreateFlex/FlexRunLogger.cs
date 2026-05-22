using System.Text.Json.Nodes;

namespace ExecuteVDICreateFlex;

internal sealed class FlexRunLogger
{
    private static readonly StringComparer s_secretKeyComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly HashSet<string> s_secretKeys = new(s_secretKeyComparer)
    {
        "adminPassword",
        "protectedSettings",
        "protectedSettingsFromKeyVault"
    };

    private readonly object _writeLock = new();

    private FlexRunLogger(string? path)
    {
        Path = path;
    }

    public string? Path { get; }

    public bool IsEnabled => Path is not null;

    public static FlexRunLogger Disabled { get; } = new(null);

    public static FlexRunLogger CreateDefault()
    {
        var logDirectory = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "logs");
        var fileName = $"execute-vdi-create-flex-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.log";
        return Create(System.IO.Path.Combine(logDirectory, fileName));
    }

    public static FlexRunLogger Create(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        var directory = System.IO.Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, string.Empty);
        return new FlexRunLogger(fullPath);
    }

    public void Info(string message) => Write("INFO", message);

    public void Warning(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERROR", message);

    public void Exception(Exception exception, string message)
    {
        Error($"{message}: {exception.GetType().Name}: {exception.Message}");
        if (!string.IsNullOrWhiteSpace(exception.StackTrace))
        {
            Write("ERROR", exception.StackTrace);
        }
    }

    public void SanitizedJson(string label, string json)
    {
        if (!IsEnabled)
        {
            return;
        }

        Info(label);
        WriteRaw(SanitizeJson(json));
    }

    private void Write(string level, string message)
    {
        if (!IsEnabled)
        {
            return;
        }

        WriteRaw($"{DateTimeOffset.UtcNow:O} [{level}] {message}");
    }

    private void WriteRaw(string message)
    {
        if (Path is null)
        {
            return;
        }

        lock (_writeLock)
        {
            File.AppendAllText(Path, message + Environment.NewLine);
        }
    }

    private static string SanitizeJson(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is null)
            {
                return "<empty JSON payload>";
            }

            Redact(node);
            return node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch (System.Text.Json.JsonException)
        {
            return "<invalid JSON payload was not written>";
        }
    }

    private static void Redact(JsonNode node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var key in jsonObject.Select(item => item.Key).ToList())
            {
                if (s_secretKeys.Contains(key))
                {
                    jsonObject[key] = "***REDACTED***";
                    continue;
                }

                var child = jsonObject[key];
                if (child is not null)
                {
                    Redact(child);
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var child in jsonArray)
            {
                if (child is not null)
                {
                    Redact(child);
                }
            }
        }
    }
}
