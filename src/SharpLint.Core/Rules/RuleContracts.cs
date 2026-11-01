using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpLint.Core.Diagnostics;

namespace SharpLint.Core.Rules;

/// <summary>Where a policy applies; this does not select an execution engine.</summary>
public enum RuleScope { SourceFile, Symbol, Type, Namespace, Project, Repository }

/// <summary>Stable rule metadata. Each rule has exactly one canonical category.</summary>
public sealed record RuleDefinition(string Id, string CategoryId, RuleScope Scope)
{
    public RuleInputRequirement InputRequirement { get; init; } = RuleInputRequirement.Syntax;
}

/// <summary>Safety prerequisite, independent of execution scope and diagnostic category.</summary>
public enum RuleInputRequirement { Syntax, ErrorFreeCompilation }

/// <summary>A configured instance. Options are interpreted and validated by its registered analyzer.</summary>
public sealed record ConfiguredRule(string RuleId, JsonElement Options, Severity Severity = Severity.Warning);

/// <summary>A provider's explicit, in-process registrations. No assembly discovery or loading occurs.</summary>
public sealed record RuleModule
{
    /// <summary>Creates an external module from standard Roslyn diagnostic analyzers.</summary>
    public RuleModule(string id, string displayName, IReadOnlyList<DiagnosticAnalyzer> analyzers)
        : this(id, [], [])
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName;
        Analyzers = Array.AsReadOnly(analyzers.ToArray());
        Rules = Array.AsReadOnly(Analyzers.SelectMany(analyzer =>
        {
            ArgumentNullException.ThrowIfNull(analyzer);
            return analyzer.SupportedDiagnostics.Select(descriptor => new RuleRegistration(
                new(descriptor.Id, descriptor.Category, RuleScope.Project), new RoslynRuleAnalyzer(analyzer, descriptor.Id)));
        }).ToArray());
    }

    /// <summary>Compatibility constructor for existing in-process host policies.</summary>
    public RuleModule(string id, IReadOnlyList<RuleCategory> categories, IReadOnlyList<RuleRegistration> rules)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        DisplayName = id;
        Categories = Array.AsReadOnly(categories.ToArray());
        Rules = Array.AsReadOnly(rules.ToArray());
    }

    public string Id { get; }
    public string DisplayName { get; init; }
    public IReadOnlyList<DiagnosticAnalyzer> Analyzers { get; } = [];
    public IReadOnlyList<RuleRegistration> Rules { get; }
    public IReadOnlyList<RuleCategory> Categories { get; }
}

public sealed record RuleRegistration(RuleDefinition Definition, IRuleAnalyzer Analyzer);

/// <summary>Read-only captured syntax and optional host-provided project compilations.</summary>
public sealed record RuleAnalysisContext(string WorkingDirectory, IReadOnlyList<SyntaxTree> SyntaxTrees)
{
    /// <summary>Project compilations supplied by the host, including references and compiler options.</summary>
    public IReadOnlyList<Compilation> Compilations { get; init; } = [];
    public IReadOnlyDictionary<Compilation, string> CompilationProjects { get; init; } = new Dictionary<Compilation, string>();
}

/// <summary>A finding; the host attaches the registered identity, category, and configured severity.</summary>
public sealed record RuleViolation(string Message, SourceLocation Location, string? Suggestion = null)
{
    public bool HasSourceLocation { get; init; } = true;
    public string? Project { get; init; }
    public string? HelpUrl { get; init; }
    public int? SpanStart { get; init; }
    public int? SpanLength { get; init; }
}

/// <summary>Implementations must be deterministic, read-only, and honor cancellation.</summary>
public interface IRuleAnalyzer
{
    ValueTask<IReadOnlyList<RuleViolation>> AnalyzeAsync(
        RuleAnalysisContext context, JsonElement options, CancellationToken cancellationToken);
}
