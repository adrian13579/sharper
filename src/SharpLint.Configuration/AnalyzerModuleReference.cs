using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpLint.Core.Rules;

namespace SharpLint.Configuration;

/// <summary>Serializable reference to a parameterless Roslyn analyzer type, resolved from the config's package.</summary>
public sealed record AnalyzerTypeReference(string AssemblyPath, string TypeName);

/// <summary>An external user's module. Its identity and descriptor categories are independent of built-in modules.</summary>
public sealed record AnalyzerModuleReference(string Id, string DisplayName, IReadOnlyList<AnalyzerTypeReference> Analyzers)
{
    private static readonly ConditionalWeakTable<DiagnosticAnalyzer, LoadLease> Leases = new();

    internal RuleModule Load()
    {
        var assemblies = new Dictionary<string, (Assembly Assembly, LoadLease Lease)>(StringComparer.Ordinal);
        var analyzers = Analyzers.Select(reference =>
        {
            if (!Path.IsPathFullyQualified(reference.AssemblyPath))
                throw new InvalidOperationException("Analyzer assembly paths must be absolute.");
            if (!assemblies.TryGetValue(reference.AssemblyPath, out var loaded))
            {
                var loadContext = new AnalyzerLoadContext(reference.AssemblyPath);
                loaded = (loadContext.LoadFromAssemblyPath(reference.AssemblyPath), new LoadLease(loadContext));
                assemblies.Add(reference.AssemblyPath, loaded);
            }
            var type = loaded.Assembly.GetType(reference.TypeName, throwOnError: true)!;
            var analyzer = Activator.CreateInstance(type) as DiagnosticAnalyzer
                ?? throw new InvalidOperationException($"'{reference.TypeName}' must be a Roslyn DiagnosticAnalyzer.");
            Leases.Add(analyzer, loaded.Lease);
            return analyzer;
        }).ToArray();
        return new(Id, DisplayName, analyzers);
    }

    // Keep one context alive while any of its analyzers are in use; release it after a check's registry is collected.
    private sealed class LoadLease(AssemblyLoadContext context)
    {
        ~LoadLease() => context.Unload();
    }

    private sealed class AnalyzerLoadContext(string path) : AssemblyLoadContext(isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(path);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Share contracts with the host so analyzer instances have the same Roslyn type identity.
            if (assemblyName.Name is "Microsoft.CodeAnalysis" or "Microsoft.CodeAnalysis.CSharp" or "SharpLint.Core")
                return Default.LoadFromAssemblyName(assemblyName);
            var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
            var sibling = Path.Combine(Path.GetDirectoryName(path)!, assemblyName.Name + ".dll");
            return resolved is not null ? LoadFromAssemblyPath(resolved)
                : File.Exists(sibling) ? LoadFromAssemblyPath(sibling) : null;
        }
    }
}
