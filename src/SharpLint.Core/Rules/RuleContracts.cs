using System.Text.Json;
using Microsoft.CodeAnalysis;
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
public sealed record RuleModule(string Id, IReadOnlyList<RuleCategory> Categories, IReadOnlyList<RuleRegistration> Rules);

public sealed record RuleRegistration(RuleDefinition Definition, IRuleAnalyzer Analyzer);

/// <summary>Read-only syntax input. Scopes requiring project or semantic information need future host support.</summary>
public sealed record RuleAnalysisContext(string WorkingDirectory, IReadOnlyList<SyntaxTree> SyntaxTrees);

/// <summary>A finding; the host attaches the registered identity, category, and configured severity.</summary>
public sealed record RuleViolation(string Message, SourceLocation Location, string? Suggestion = null);

/// <summary>Implementations must be deterministic, read-only, and honor cancellation.</summary>
public interface IRuleAnalyzer
{
    ValueTask<IReadOnlyList<RuleViolation>> AnalyzeAsync(
        RuleAnalysisContext context, JsonElement options, CancellationToken cancellationToken);
}
