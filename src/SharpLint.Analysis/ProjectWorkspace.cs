using System.Runtime.CompilerServices;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using SharpLint.Configuration;
using SharpLint.Core.Analysis;

namespace SharpLint.Analysis;

internal static class ProjectWorkspace
{
    internal const string LooseProject = "SharpLint loose sources";
    internal const string PolicyProject = "SharpLint policy-only sources";
    private static readonly object s_registrationGate = new();
    private static string? s_msbuildPath;

    internal static async Task<Workspace> LoadAsync(string root, CancellationToken ct)
    {
        var projects = SourceFiles.EnumerateInputs(root)
            .Where(path => Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (projects.Length == 0) return new AdhocWorkspace();
        lock (s_registrationGate)
        {
            var instance = MSBuildLocator.QueryVisualStudioInstances(new VisualStudioInstanceQueryOptions
                { WorkingDirectory = root, DiscoveryTypes = VisualStudioInstanceQueryOptions.Default.DiscoveryTypes })
                .FirstOrDefault() ?? throw new InvalidOperationException("No MSBuild SDK is available for the analysis root.");
            if (!MSBuildLocator.IsRegistered)
            {
                MSBuildLocator.RegisterInstance(instance);
                s_msbuildPath = instance.MSBuildPath;
            }
            else if (s_msbuildPath is not null && s_msbuildPath != instance.MSBuildPath)
                throw new InvalidOperationException("The selected MSBuild SDK changed. Restart the daemon or use --no-daemon.");
        }
        return await LoadMSBuildAsync(projects, ct).ConfigureAwait(false);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<Workspace> LoadMSBuildAsync(string[] paths, CancellationToken ct)
    {
        var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>
        {
            ["DesignTimeBuild"] = "true", ["BuildProjectReferences"] = "false",
            ["SkipCompilerExecution"] = "true",
        });
        workspace.SkipUnrecognizedProjects = false;
        workspace.LoadMetadataForReferencedProjects = false;
        try
        {
            foreach (var path in paths)
            {
                if (!workspace.CurrentSolution.Projects.Any(p => p.FilePath == path))
                    await workspace.OpenProjectAsync(path, cancellationToken: ct).ConfigureAwait(false);
            }
            var failures = workspace.Diagnostics.Where(d => d.Kind == WorkspaceDiagnosticKind.Failure).ToArray();
            if (failures.Length > 0)
                throw new InvalidOperationException("Project analysis failed: " + string.Join("; ", failures.Select(d => d.Message)));
            if (workspace.CurrentSolution.Projects.Any(p => p.Language != LanguageNames.CSharp))
                throw new InvalidOperationException("Only C# project compilations are supported.");
            return workspace;
        }
        catch { workspace.Dispose(); throw; }
    }

    // Evaluation records the actual imported files, including imports outside the requested root.
    // No MSBuild targets are executed here. Locator must be registered before this method is JITted.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static HashSet<string> CompilationInputs(Solution solution)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        using var collection = new Microsoft.Build.Evaluation.ProjectCollection();
        foreach (var project in solution.Projects)
        {
            if (project.FilePath is { } projectPath)
            {
                paths.Add(projectPath);
                var evaluated = collection.LoadProject(projectPath, new Dictionary<string, string>
                {
                    ["DesignTimeBuild"] = "true", ["BuildProjectReferences"] = "false", ["SkipCompilerExecution"] = "true",
                }, null);
                foreach (var import in evaluated.Imports) paths.Add(import.ImportedProject.FullPath);
                TrackOptionalImports(evaluated, paths);
                var frameworks = evaluated.GetPropertyValue("TargetFrameworks").Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var framework in frameworks)
                {
                    var inner = collection.LoadProject(projectPath, new Dictionary<string, string>
                    {
                        ["DesignTimeBuild"] = "true", ["BuildProjectReferences"] = "false",
                        ["SkipCompilerExecution"] = "true", ["TargetFramework"] = framework,
                    }, null);
                    foreach (var import in inner.Imports) paths.Add(import.ImportedProject.FullPath);
                    TrackOptionalImports(inner, paths);
                }
                foreach (var name in new[] { "ProjectAssetsFile", "MSBuildProjectExtensionsPath", "CodeAnalysisRuleSet", "AssemblyOriginatorKeyFile" })
                {
                    var value = evaluated.GetPropertyValue(name);
                    if (string.IsNullOrEmpty(value)) continue;
                    var path = Path.GetFullPath(value, Path.GetDirectoryName(projectPath)!);
                    paths.Add(name == "MSBuildProjectExtensionsPath" ? Path.Combine(path, "project.assets.json") : path);
                    if (name == "CodeAnalysisRuleSet") paths.UnionWith(RuleSet.GetEffectiveIncludesFromFile(path));
                }
            }
            foreach (var document in project.Documents.Cast<TextDocument>().Concat(project.AdditionalDocuments).Concat(project.AnalyzerConfigDocuments))
                if (document.FilePath is { } path) paths.Add(path);
            foreach (var reference in project.MetadataReferences.OfType<PortableExecutableReference>())
                if (reference.FilePath is { } path) paths.Add(path);
            foreach (var reference in project.AnalyzerReferences)
                if (reference.FullPath is { } path) paths.Add(path);
        }
        return paths;
    }

    private static void TrackOptionalImports(Microsoft.Build.Evaluation.Project project, HashSet<string> paths)
    {
        var sdk = project.GetPropertyValue("MSBuildToolsPath");
        var files = project.Imports.Select(i => i.ImportedProject).Prepend(project.Xml);
        foreach (var file in files)
        {
            // The installed SDK is a fixed analysis environment; record its actual imports, not its optional probes.
            if (!string.IsNullOrEmpty(sdk) && file.FullPath.StartsWith(sdk + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            foreach (var import in file.Imports.Where(i => string.IsNullOrEmpty(i.Sdk)))
            {
                var expression = import.Project.Replace("$(MSBuildThisFileDirectory)", Path.GetDirectoryName(file.FullPath) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);
                var expanded = project.ExpandString(expression).Replace('\\', Path.DirectorySeparatorChar);
                foreach (var value in expanded.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var path = Path.GetFullPath(value, Path.GetDirectoryName(file.FullPath)!);
                    var wildcard = path.IndexOfAny(['*', '?']);
                    paths.Add(wildcard < 0 ? path : Path.GetDirectoryName(path[..wildcard])!);
                }
            }
        }
    }

    internal static async Task<Solution> FreezeReferencesAsync(Solution solution, CancellationToken ct)
    {
        var images = new Dictionary<string, System.Collections.Immutable.ImmutableArray<byte>>(StringComparer.Ordinal);
        foreach (var project in solution.Projects.ToArray())
        {
            var references = new List<MetadataReference>();
            foreach (var reference in project.MetadataReferences)
            {
                if (reference is not PortableExecutableReference { FilePath: { } path })
                    throw new InvalidOperationException($"Cannot capture metadata reference '{reference.Display}'.");
                if (!images.TryGetValue(path, out var image))
                {
                    image = System.Collections.Immutable.ImmutableArray.CreateRange(await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false));
                    images.Add(path, image);
                }
                references.Add(MetadataReference.CreateFromImage(image, reference.Properties, filePath: path));
            }
            solution = solution.WithProjectMetadataReferences(project.Id, references);
        }
        return solution;
    }

    internal static Solution AddLooseDocuments(Solution solution, InputSnapshot inputs)
    {
        foreach (var project in solution.Projects.ToArray())
        {
            solution = solution.WithProjectAnalyzerReferences(project.Id, []);
            foreach (var document in project.Documents.Where(d => Path.GetFileName(d.FilePath) == ConfigLoader.DefaultFileName))
                solution = solution.RemoveDocument(document.Id);
        }
        var owned = solution.Projects.SelectMany(p => p.Documents).Select(d => d.FilePath).ToHashSet(StringComparer.Ordinal);
        var hasProjects = solution.ProjectIds.Count > 0;
        foreach (var group in inputs.Sources.Where(s => !owned.Contains(s.Key)).GroupBy(s =>
                     hasProjects || Path.GetFileName(s.Key) == ConfigLoader.DefaultFileName ? PolicyProject : LooseProject))
        {
            var id = ProjectId.CreateNewId();
            solution = solution.AddProject(id, group.Key, group.Key, LanguageNames.CSharp)
                .WithProjectCompilationOptions(id, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            if (group.Key == LooseProject)
            {
                var assemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
                var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);
                solution = solution.WithProjectMetadataReferences(id, assemblies
                    .Where(path => Path.GetDirectoryName(path) == runtimeDirectory).Select(path => MetadataReference.CreateFromFile(path)));
            }
            foreach (var (path, input) in group)
                solution = solution.AddDocument(DocumentId.CreateNewId(id), Path.GetFileName(path), SourceText.From(input.Text), filePath: path);
        }
        return solution;
    }
}
