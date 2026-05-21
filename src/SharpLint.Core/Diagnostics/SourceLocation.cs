namespace SharpLint.Core.Diagnostics;

public readonly record struct SourceLocation(string FilePath, int Line, int Column);
