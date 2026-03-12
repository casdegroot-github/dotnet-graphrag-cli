using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace GraphRagCli.Features.Ingest.Analysis;

public class CodeAnalyzer : ICodeAnalyzer
{
    public async Task<Dictionary<string, AnalysisResult>> AnalyzeSolutionAsync(
        string solutionPath, bool skipTests, bool skipSamples)
    {
        Console.WriteLine($"Loading: {solutionPath}");
        using var workspace = MSBuildWorkspace.Create();
        workspace.RegisterWorkspaceFailedHandler(e =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                Console.WriteLine($"  Workspace warning: {e.Diagnostic.Message}");
        });

        IReadOnlyList<Project> projects;
        if (solutionPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            var project = await workspace.OpenProjectAsync(solutionPath);
            projects = [project];
            Console.WriteLine($"Loaded project: {project.Name}");
        }
        else
        {
            var solution = await workspace.OpenSolutionAsync(solutionPath);
            projects = solution.Projects.ToList();
            Console.WriteLine($"Loaded {projects.Count} projects");
        }

        var results = new Dictionary<string, AnalysisResult>();

        foreach (var project in projects)
        {
            var projectName = project.Name;

            if (skipTests && IsTestProject(project))
            {
                Console.WriteLine($"  Skipping test project: {projectName}");
                continue;
            }

            if (skipSamples && (projectName.Contains("Sample", StringComparison.OrdinalIgnoreCase) ||
                                projectName.Contains("Example", StringComparison.OrdinalIgnoreCase) ||
                                projectName.Contains("Playground", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine($"  Skipping sample project: {projectName}");
                continue;
            }

            var compilation = await project.GetCompilationAsync();
            if (compilation == null)
            {
                Console.WriteLine($"  Warning: Could not compile {projectName}");
                continue;
            }

            var analysisResult = await AnalyzeProjectAsync(compilation);

            Console.WriteLine($"Extracted from {projectName}: {analysisResult.Namespaces.Count} namespaces, " +
                $"{analysisResult.Classes.Count} classes, {analysisResult.Interfaces.Count} interfaces, " +
                $"{analysisResult.Methods.Count} methods, {analysisResult.Calls.Count} calls, " +
                $"{analysisResult.References.Count} references, {analysisResult.Enums.Count} enums");

            if (analysisResult.Classes.Count > 0 || analysisResult.Interfaces.Count > 0)
                results[projectName] = analysisResult;
            else
                Console.WriteLine($"  (no classes/interfaces found, skipping)");
        }

        return results;
    }

    static bool IsTestProject(Project project)
    {
        var testIndicators = new[] { "Microsoft.NET.Test.Sdk", "xunit", "nunit", "mstest", "BenchmarkDotNet" };
        return project.MetadataReferences
            .Any(r => testIndicators.Any(t => r.Display?.Contains(t, StringComparison.OrdinalIgnoreCase) == true));
    }

    static async Task<AnalysisResult> AnalyzeProjectAsync(Compilation compilation)
    {
        var namespaces = new List<NamespaceInfo>();
        var classes = new List<ClassInfo>();
        var interfaces = new List<InterfaceInfo>();
        var methods = new List<MethodInfo>();
        var calls = new List<CallInfo>();
        var typeRefs = new List<ReferenceInfo>();
        var enums = new List<EnumInfo>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var filePath = tree.FilePath;

            if (string.IsNullOrEmpty(filePath) ||
                filePath.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                filePath.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                continue;

            try
            {
                var semanticModel = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync();
                var walker = new CodeSyntaxWalker(semanticModel, filePath, compilation.AssemblyName ?? "Unknown",
                    namespaces, classes, interfaces, methods, calls, typeRefs, enums);
                walker.Visit(root);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Warning: Failed to analyze {Path.GetFileName(filePath)}: {ex.Message}");
            }
        }

        return new AnalysisResult(namespaces, classes, interfaces, methods, calls, typeRefs, enums);
    }

}
