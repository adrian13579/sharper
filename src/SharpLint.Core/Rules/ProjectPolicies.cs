using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using SharpLint.Core.Analysis;

namespace SharpLint.Core.Rules;

/// <summary>Checks literal project declarations, without claiming to evaluate MSBuild conditions or imports.</summary>
internal static class ProjectPolicies
{
    internal static async ValueTask<IReadOnlyList<RuleViolation>> AnalyzeAsync(string id, RuleAnalysisContext context,
        JsonElement options, CancellationToken cancellationToken)
    {
        var packages = id == "dependencies.forbidden-packages" ? BuiltInPolicies.Strings(options, "packages") : [];
        var allowed = id == "project.target-frameworks" ? BuiltInPolicies.Strings(options, "allowed") : [];
        var results = new List<RuleViolation>();
        var files = SourceFiles.EnumerateInputs(context.WorkingDirectory);
        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Path.GetExtension(path) != ".csproj" && Path.GetFileName(path) != "Directory.Packages.props") continue;
            using var stream = File.OpenRead(path);
            var document = await XDocument.LoadAsync(stream, LoadOptions.SetLineInfo, cancellationToken).ConfigureAwait(false);
            if (id == "dependencies.forbidden-packages")
            {
                foreach (var element in document.Descendants().Where(e => e.Name.LocalName is "PackageReference" or "PackageVersion"))
                {
                    var package = (string?)element.Attribute("Include") ?? (string?)element.Attribute("Update");
                    if (package is not null && packages.Contains(package, StringComparer.OrdinalIgnoreCase))
                        Add(element, $"Package '{package}' is forbidden.");
                }
            }
            else if (Path.GetExtension(path) == ".csproj")
            {
                if (id == "project.nullable-enabled")
                {
                    var declarations = document.Descendants().Where(e => e.Name.LocalName == "Nullable").ToArray();
                    if (declarations.Length == 0) Add(document.Root!, "Declare <Nullable>enable</Nullable> in the project.");
                    foreach (var element in declarations.Where(e => e.Value.Trim() != "enable"))
                        Add(element, "Nullable declaration must be 'enable'.");
                }
                else
                {
                    var declarations = document.Descendants().Where(e => e.Name.LocalName is "TargetFramework" or "TargetFrameworks").ToArray();
                    if (declarations.Length == 0) Add(document.Root!, "Declare an allowed target framework in the project.");
                    foreach (var element in declarations)
                        foreach (var framework in element.Value.Split(';', StringSplitOptions.TrimEntries))
                            if (!allowed.Contains(framework, StringComparer.Ordinal))
                                Add(element, $"Target framework '{framework}' is not allowed.");
                }
            }

            void Add(XElement element, string message)
            {
                var line = (IXmlLineInfo)element;
                results.Add(new(message, new(path, line.LineNumber, line.LinePosition)));
            }
        }
        return results;
    }
}
