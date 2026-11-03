using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SharpLint.Cli;

/// <summary>Edit only the owned server entry, preserving other JSONC settings and comments.</summary>
internal static class JsonConfigEdit
{
    internal static string SetServer(string text, string collection, JsonObject entry, int existingServers)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        reader.Read();
        var rootStart = checked((int)reader.TokenStartIndex);
        var parent = Find(bytes, rootStart, collection);
        if (parent is null)
        {
            var count = (JsonNode.Parse(text, documentOptions: new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject)!.Count;
            return Insert(bytes, rootStart, collection, new JsonObject { ["sharplint"] = entry }, count);
        }
        var existing = Find(bytes, parent.Value.Start, "sharplint");
        if (existing is null) return Insert(bytes, parent.Value.Start, "sharplint", entry, existingServers);
        var indent = Indent(bytes, existing.Value.Property);
        var value = Format(entry, indent, text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n");
        return Encoding.UTF8.GetString(bytes.AsSpan(0, existing.Value.Start)) + value + Encoding.UTF8.GetString(bytes.AsSpan(existing.Value.End));
    }

    private static string Insert(byte[] bytes, int start, string name, JsonNode value, int count)
    {
        var text = Encoding.UTF8.GetString(bytes);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var indent = Indent(bytes, start) + "  ";
        var addition = newline + indent + JsonSerializer.Serialize(name) + ": " + Format(value, indent, newline) + (count > 0 ? "," : "") + newline;
        return Encoding.UTF8.GetString(bytes.AsSpan(0, start + 1)) + addition + Encoding.UTF8.GetString(bytes.AsSpan(start + 1));
    }

    private static string Format(JsonNode value, string indent, string newline)
        => value.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\n", newline + indent, StringComparison.Ordinal);

    private static string Indent(byte[] bytes, int position)
    {
        var line = position;
        while (line > 0 && bytes[line - 1] != '\n') line--;
        var end = line;
        while (end < bytes.Length && bytes[end] is (byte)' ' or (byte)'\t') end++;
        return Encoding.UTF8.GetString(bytes.AsSpan(line, end - line));
    }

    private static Range? Find(byte[] bytes, int start, string name)
    {
        var reader = new Utf8JsonReader(bytes.AsSpan(start), new JsonReaderOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        reader.Read();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var property = checked(start + (int)reader.TokenStartIndex);
            var key = reader.GetString();
            reader.Read();
            var value = checked(start + (int)reader.TokenStartIndex);
            reader.Skip();
            if (key == name) return new(property, value, checked(start + (int)reader.BytesConsumed));
        }
        return null;
    }

    private readonly record struct Range(int Property, int Start, int End);
}
