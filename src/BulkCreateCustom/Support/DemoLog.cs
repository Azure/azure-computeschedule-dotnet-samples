using System.ClientModel.Primitives;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BulkCreateCustom;

internal sealed class DemoLog(TextWriter writer, string? password, TextWriter? fileWriter = null, bool verbose = false)
{
    public void Write(string message) => WriteMessage(message, detail: false);
    public void WriteDetail(string message) => WriteMessage(message, detail: true);

    private void WriteMessage(string message, bool detail)
    {
        var safe = string.IsNullOrEmpty(password) ? message : message.Replace(password, "[REDACTED]", StringComparison.Ordinal);
        safe = new string(safe.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        lock (writer)
            WriteLine(safe, detail);
    }

    public void WriteRequest(BatchRequest batch) => WriteJson(
        $"Batch {batch.Label} request JSON (secrets redacted):",
        ModelReaderWriter.Write(batch.Data, new ModelReaderWriterOptions("W")));

    public void WriteJson(string label, BinaryData data)
    {
        var json = JsonNode.Parse(data.ToString())
            ?? throw new InvalidDataException("SDK request serialization returned no JSON.");
        Redact(json);
        lock (writer)
        {
            WriteLine(label, detail: true);
            WriteLine(json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), detail: true);
        }
    }

    private void WriteLine(string text, bool detail)
    {
        // Never discard detail when there is no file destination (e.g. an offline harness).
        if (!detail || verbose || fileWriter is null)
            writer.WriteLine(text);
        fileWriter?.WriteLine(text);
        fileWriter?.Flush();
    }

    private void Redact(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var (key, value) in obj.ToArray())
            {
                if (key.ToLowerInvariant() is "adminpassword" or "password" or "secrets"
                    or "protectedsettings" or "protectedsettingsfromkeyvault" or "customdata" or "userdata")
                    obj[key] = "[REDACTED]";
                else if (value is not null)
                    Redact(value);
            }
        }
        else if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
                if (array[i] is { } value)
                    Redact(value);
        }
        else if (!string.IsNullOrEmpty(password) && node is JsonValue scalar && scalar.TryGetValue<string>(out var text)
            && text.Contains(password, StringComparison.Ordinal))
        {
            node.ReplaceWith(JsonValue.Create(text.Replace(password, "[REDACTED]", StringComparison.Ordinal)));
        }
    }
}
