namespace SharpLint.Analysis;

public sealed class DocumentState(string text)
{
    public string Text { get; private set; } = text;
    public int Version { get; private set; } = 1;
    public string? ResultId { get; set; }
    public IReadOnlyList<SharpLintDiagnostic> Diagnostics { get; set; } = [];

    public object Change(string text, int syncKind)
    {
        if (syncKind is not (1 or 2)) throw new NotSupportedException("The server does not support document changes.");
        object change = syncKind == 1 ? new { text } : new
        {
            range = new { start = new { line = 0, character = 0 }, end = EndPosition(Text) },
            text,
        };
        Text = text;
        Version = checked(Version + 1);
        return change;
    }

    private static object EndPosition(string text)
    {
        var line = 0;
        var character = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is '\r' or '\n')
            {
                if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                line++;
                character = 0;
            }
            else character++; // .NET strings count UTF-16 code units, matching LSP negotiation.
        }
        return new { line, character };
    }
}
