using System.Reflection;
using System.Xml.Linq;
using SharpLint.Analysis;
using SharpLint.Tests.Lsp;

namespace SharpLint.Tests;

public sealed class ConnectorArchitectureTests
{
    [Fact]
    public void Production_solution_contains_the_service_and_its_agent_host()
    {
        var root = ServiceTests.RepositoryRoot();
        var solution = XDocument.Load(Path.Combine(root, "SharpLint.slnx"));
        var projects = solution.Descendants("Project").Select(p => p.Attribute("Path")!.Value)
            .Where(p => p.StartsWith("src/", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "src/SharpLint.Analysis/SharpLint.Analysis.csproj", "src/SharpLint.Cli/SharpLint.Cli.csproj" }, projects);
    }

    [Fact]
    public void Connector_assemblies_do_not_embed_a_compiler_workspace_or_policy_engine()
    {
        var serviceReferences = typeof(DiagnosticService).Assembly.GetReferencedAssemblies();
        var hostReferences = Assembly.LoadFrom(RoslynFixture.CliPath).GetReferencedAssemblies();
        Assert.Contains(hostReferences, a => a.Name == "SharpLint.Analysis");
        Assert.All(serviceReferences.Concat(hostReferences), reference =>
        {
            Assert.False(reference.Name!.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal), reference.FullName);
            Assert.False(reference.Name.StartsWith("Microsoft.Build", StringComparison.Ordinal), reference.FullName);
            Assert.False(reference.Name is "SharpLint.Core" or "SharpLint.Configuration", reference.FullName);
        });
    }
}
