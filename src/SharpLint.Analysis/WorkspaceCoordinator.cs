using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using SharpLint.Configuration;
using SharpLint.Core.Analysis;
using SharpLint.Core.Rules;
using Diagnostic = SharpLint.Core.Diagnostics.Diagnostic;

namespace SharpLint.Analysis;

/// <summary>Owns the current Roslyn workspace, document policy cache and project compiler cache. Never applies source writes.</summary>
public sealed class WorkspaceCoordinator : IDisposable
{
    private sealed record CachedDocument(string Fingerprint, IReadOnlyList<Diagnostic> Diagnostics);

    private readonly string _root;
    private readonly Func<CancellationToken, Task<SharpLintConfig>> _loadConfiguration;
    private readonly RuleRegistry _registry;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Workspace? _workspace;
    private HashSet<string> _compilationInputs = new(StringComparer.Ordinal);
    private Dictionary<ProjectId, IReadOnlyList<Diagnostic>> _compilerCache = new();
    private readonly FileSystemWatcher? _watcher;
    private Dictionary<DocumentId, CachedDocument> _cache = new();
    private SharpLintConfig? _configuration;
    private string? _inputFingerprint;
    private long _changeEpoch;

    public WorkspaceSnapshot? CurrentSnapshot { get; private set; }

    public WorkspaceCoordinator(string root, Func<CancellationToken, Task<SharpLintConfig>>? loadConfiguration = null,
        RuleRegistry? registry = null, bool watch = true)
    {
        _root = Path.GetFullPath(root);
        if (!Directory.Exists(_root)) throw new DirectoryNotFoundException(_root);
        _registry = registry ?? RuleRegistry.Default;
        _loadConfiguration = loadConfiguration ?? (ct =>
        {
            var path = Path.Combine(_root, ConfigLoader.DefaultFileName);
            return File.Exists(path) ? ConfigLoader.LoadAsync(path, ct) : Task.FromResult(ConfigLoader.Default());
        });
        if (watch)
        {
            _watcher = new FileSystemWatcher(_root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            _watcher.Changed += OnChange;
            _watcher.Created += OnChange;
            _watcher.Deleted += OnChange;
            _watcher.Renamed += (sender, args) =>
            {
                OnChange(sender, args);
                OnChange(sender, new FileSystemEventArgs(WatcherChangeTypes.Deleted,
                    Path.GetDirectoryName(args.OldFullPath)!, Path.GetFileName(args.OldFullPath)));
            };
            _watcher.Error += (_, _) => Interlocked.Increment(ref _changeEpoch);
            _watcher.EnableRaisingEvents = true;
        }
    }

    private void OnChange(object sender, FileSystemEventArgs args)
    {
        var relative = Path.GetRelativePath(_root, args.FullPath);
        if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or ".git")) return;
        if (InputSnapshot.IsRelevant(args.FullPath)) Interlocked.Increment(ref _changeEpoch);
    }

    public async Task<SnapshotResult> CheckAsync(bool full = false, CancellationToken cancellationToken = default)
    {
        // Queued clients reuse the newly published cache. A canceled waiter never cancels another client's work.
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var result = await CheckStableAsync(full, cancellationToken).ConfigureAwait(false);
                    if (result is not null) return result;
                }
                catch (IOException) when (attempt < 2)
                {
                    // A rename/delete while capturing inputs makes the observation uncertain. Retry from disk.
                }
            }
            throw new InvalidOperationException("Repository changed during analysis; no validated snapshot could be produced. Retry the check.");
        }
        finally { _gate.Release(); }
    }

    private async Task<SnapshotResult?> CheckStableAsync(bool full, CancellationToken cancellationToken)
    {
        var total = Stopwatch.StartNew();
        var phase = Stopwatch.StartNew();
        var epoch = Volatile.Read(ref _changeEpoch);
        var inputs = await InputSnapshot.ReadAsync(_root, cancellationToken,
            compilationInputs: _compilationInputs).ConfigureAwait(false);
        var reconciliation = phase.Elapsed.TotalMilliseconds;
        var inputsChanged = _inputFingerprint != inputs.Fingerprint;
        var reloadConfiguration = full || inputsChanged || _configuration is null;
        var configuration = reloadConfiguration
            ? await _loadConfiguration(cancellationToken).ConfigureAwait(false) : _configuration!;
        var analyzerInputs = configuration.Modules?.SelectMany(m => m.Analyzers).Select(a => a.AssemblyPath).ToArray() ?? [];
        if (analyzerInputs.Any(path => !_compilationInputs.Contains(path)))
        {
            _compilationInputs.UnionWith(analyzerInputs);
            return null;
        }
        var configurationHash = reloadConfiguration
            ? InputSnapshot.Hash(JsonSerializer.Serialize(configuration)) : CurrentSnapshot!.ConfigurationFingerprint;
        var invalidateNaming = full || CurrentSnapshot?.ConfigurationFingerprint != configurationHash
            || CurrentSnapshot?.ProjectGraphFingerprint != inputs.ProjectFingerprint;
        var version = (CurrentSnapshot?.Version ?? 0) + (inputsChanged || invalidateNaming ? 1 : 0);
        phase.Restart();
        var previous = CurrentSnapshot?.Solution;
        // Changes to source membership can change MSBuild globs. Metadata changes need reevaluation too.
        var oldPaths = previous?.Projects.SelectMany(p => p.Documents).Select(d => d.FilePath)
            .Where(p => p is not null).ToHashSet(StringComparer.Ordinal) ?? [];
        var reload = full || previous is null || CurrentSnapshot!.ProjectGraphFingerprint != inputs.ProjectFingerprint
            || !oldPaths.SetEquals(inputs.Sources.Keys);
        Workspace? candidate = null;
        try
        {
            var solution = previous;
            if (reload)
            {
                candidate = await ProjectWorkspace.LoadAsync(_root, cancellationToken).ConfigureAwait(false);
                var discovered = candidate is Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace
                    ? ProjectWorkspace.CompilationInputs(candidate.CurrentSolution) : new HashSet<string>(StringComparer.Ordinal);
                // Newly discovered inputs must be observed BEFORE loading options/references, then again after analysis.
                // Retry discovery rather than claiming the first post-load observation validates those options.
                if (!discovered.IsSubsetOf(_compilationInputs))
                {
                    _compilationInputs.UnionWith(discovered);
                    return null;
                }
                solution = ProjectWorkspace.AddLooseDocuments(candidate.CurrentSolution, inputs);
                solution = await ProjectWorkspace.FreezeReferencesAsync(solution, cancellationToken).ConfigureAwait(false);
            }
            var changedProjects = new HashSet<ProjectId>();
            foreach (var project in solution!.Projects.ToArray())
            {
                foreach (var document in project.Documents)
                {
                    if (document.FilePath is not { } path || !inputs.Sources.TryGetValue(path, out var input))
                        throw new InvalidOperationException($"Source input was not captured: {document.FilePath}");
                    if (reload || !_cache.TryGetValue(document.Id, out var cached) || cached.Fingerprint != input.Fingerprint)
                    {
                        solution = solution.WithDocumentText(document.Id, SourceText.From(input.Text));
                        changedProjects.Add(project.Id);
                    }
                }
            }
            var next = new ConcurrentDictionary<DocumentId, CachedDocument>();
            var work = new List<Document>();
            foreach (var document in solution.Projects.SelectMany(p => p.Documents))
            {
                var input = inputs.Sources[document.FilePath!];
                var previousDocument = reload ? previous?.Projects
                    .Where(p => CompilerDiagnosticAdapter.ProjectIdentity(p) == CompilerDiagnosticAdapter.ProjectIdentity(document.Project)
                        && Equals(p.ParseOptions, document.Project.ParseOptions))
                    .SelectMany(p => p.Documents).FirstOrDefault(d => d.FilePath == document.FilePath) : document;
                if (!invalidateNaming && previousDocument is not null
                    && _cache.TryGetValue(previousDocument.Id, out var cached) && cached.Fingerprint == input.Fingerprint)
                    next[document.Id] = cached;
                else work.Add(document);
            }
            var workspaceMs = phase.Elapsed.TotalMilliseconds;
            phase.Restart();
            var analyzer = new Analyzer();
            var context = new AnalysisContext { WorkingDirectory = _root, NamingRules = configuration.NamingRules };
            await Parallel.ForEachAsync(work, new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
                CancellationToken = cancellationToken,
            }, async (document, ct) =>
            {
                var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
                var result = await analyzer.AnalyzeTreesAsync(context, IsPolicySource(document.FilePath!) ? [tree!] : [], ct).ConfigureAwait(false);
                var identity = document.Project.FilePath is null ? null : CompilerDiagnosticAdapter.ProjectIdentity(document.Project);
                next[document.Id] = new CachedDocument(inputs.Sources[document.FilePath!].Fingerprint,
                    result.Diagnostics.Select(d => d with { Project = identity }).ToArray());
            }).ConfigureAwait(false);
            var diagnostics = next.Values.SelectMany(entry => entry.Diagnostics).ToList();
            var graph = solution.GetProjectDependencyGraph();
            var affected = changedProjects.SelectMany(p => graph.GetProjectsThatTransitivelyDependOnThisProject(p))
                .Concat(changedProjects).ToHashSet();
            var compilerNext = new Dictionary<ProjectId, IReadOnlyList<Diagnostic>>();
            var projectsAnalyzed = 0;
            var projectsReused = 0;
            // A single bounded project worker avoids oversubscribing Roslyn's own compilation concurrency.
            foreach (var project in solution.Projects.Where(p => p.FilePath is not null || p.Name != ProjectWorkspace.PolicyProject))
            {
                if (!reload && !affected.Contains(project.Id) && _compilerCache.TryGetValue(project.Id, out var cached))
                {
                    compilerNext[project.Id] = cached;
                    projectsReused++;
                }
                else
                {
                    var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidOperationException($"Cannot construct compilation for '{project.FilePath ?? project.Name}'.");
                    // GetDiagnostics respects warning options, suppression and warnings-as-errors. Configured module analyzers execute separately below;
                    // source generators are not executed by this host.
                    compilerNext[project.Id] = compilation.GetDiagnostics(cancellationToken)
                        .Where(d => !d.IsSuppressed && d.Severity != DiagnosticSeverity.Hidden)
                        .Select(d => CompilerDiagnosticAdapter.Convert(d, CompilerDiagnosticAdapter.ProjectIdentity(project))).ToArray();
                    projectsAnalyzed++;
                }
            }
            diagnostics.AddRange(compilerNext.Values.SelectMany(d => d));
            IReadOnlyList<AnalysisFailure> failures = [];
            var ruleRegistry = configuration.Modules is { Count: > 0 } ? configuration.CreateRegistry(_registry) : _registry;
            if (configuration.Rules is { Count: > 0 })
            {
                var compilations = new List<Compilation>();
                var compilationProjects = new Dictionary<Compilation, string>();
                foreach (var project in solution.Projects)
                {
                    var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidOperationException($"Cannot construct compilation for '{project.Name}'.");
                    compilations.Add(compilation);
                    if (project.FilePath is not null)
                        compilationProjects.Add(compilation, CompilerDiagnosticAdapter.ProjectIdentity(project));
                }
                var trees = new List<SyntaxTree>();
                foreach (var document in solution.Projects.SelectMany(p => p.Documents).Where(d => IsPolicySource(d.FilePath!))
                             .OrderBy(d => d.FilePath, StringComparer.Ordinal))
                    trees.Add((await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false))!);
                var moduleResult = await analyzer.AnalyzeTreesAsync(new AnalysisContext
                {
                    WorkingDirectory = _root, NamingRules = [], Rules = configuration.Rules, Registry = ruleRegistry,
                    Compilations = compilations, CompilationProjects = compilationProjects,
                    CompilationHasErrors = compilerNext.Count == 0 ? null : diagnostics.Any(d => d.Origin == SharpLint.Core.Diagnostics.DiagnosticOrigin.Compiler
                        && d.Severity == SharpLint.Core.Diagnostics.Severity.Error),
                }, trees, cancellationToken).ConfigureAwait(false);
                diagnostics.AddRange(moduleResult.Diagnostics);
                failures = moduleResult.Failures;
            }
            var executionMs = phase.Elapsed.TotalMilliseconds;
            phase.Restart();
            var ordered = DiagnosticAggregator.Aggregate(diagnostics);
            var aggregationMs = phase.Elapsed.TotalMilliseconds;
            phase.Restart();
            var verified = await InputSnapshot.ReadAsync(_root, cancellationToken, includeSources: false, compilationInputs: _compilationInputs).ConfigureAwait(false);
            reconciliation += phase.Elapsed.TotalMilliseconds;
            if (inputs.Fingerprint != verified.Fingerprint || epoch != Volatile.Read(ref _changeEpoch)) return null;

            // Publish only after validation. Failed/canceled attempts never replace the last successful state.
            _inputFingerprint = inputs.Fingerprint;
            _configuration = configuration;
            _cache = new Dictionary<DocumentId, CachedDocument>(next);
            _compilerCache = compilerNext;
            if (candidate is not null)
            {
                _workspace?.Dispose();
                _workspace = candidate;
                candidate = null;
            }
            CurrentSnapshot = new WorkspaceSnapshot(version, solution, configurationHash, inputs.ProjectFingerprint, inputs.Fingerprint);
            var resultValue = new AnalysisResult(ordered, total.Elapsed, inputs.Sources.Count) { Failures = failures };
            return new SnapshotResult(version, resultValue, new CheckStatistics(work.Count, next.Count - work.Count,
                next.Count - work.Count, reconciliation, workspaceMs, executionMs, aggregationMs, total.Elapsed.TotalMilliseconds)
            {
                ProjectsAnalyzed = projectsAnalyzed, ProjectsReused = projectsReused,
                CompilerDiagnostics = ordered.Count(d => d.Origin == SharpLint.Core.Diagnostics.DiagnosticOrigin.Compiler),
                SharpLintDiagnostics = ordered.Count(d => d.Origin == SharpLint.Core.Diagnostics.DiagnosticOrigin.SharpLint),
            }, inputs.Fingerprint);
        }
        finally { candidate?.Dispose(); }
    }

    private bool IsPolicySource(string path) => !Path.GetRelativePath(_root, path).Split(Path.DirectorySeparatorChar)
        .Any(part => part is "bin" or "obj" or ".git" or ".sharplint");

    public void Dispose()
    {
        _watcher?.Dispose();
        _workspace?.Dispose();
        _gate.Dispose();
    }
}
