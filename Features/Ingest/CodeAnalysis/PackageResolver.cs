using System.Diagnostics;
using System.Text.Json;

namespace GraphRagCli.Features.Ingest.Analysis;

public class PackageResolver
{
    public Dictionary<string, List<string>> ResolvePackages(
        string solutionPath,
        IReadOnlyList<(string Name, string FilePath)> projects)
    {
        var solutionDir = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;

        // Strategy 1: look for a *.NuGet.slnf next to the solution
        var packageMap = ResolveFromSlnf(solutionDir, projects);
        if (packageMap != null)
        {
            PrintResults(packageMap, projects.Count, "slnf");
            return packageMap;
        }

        // Strategy 2: use dotnet msbuild -getProperty:IsPackable with full evaluation
        packageMap = ResolveFromMsBuild(projects);
        PrintResults(packageMap, projects.Count, "msbuild");
        return packageMap;
    }

    private static Dictionary<string, List<string>>? ResolveFromSlnf(
        string solutionDir,
        IReadOnlyList<(string Name, string FilePath)> projects)
    {
        var slnfFiles = Directory.GetFiles(solutionDir, "*.NuGet.slnf", SearchOption.TopDirectoryOnly);
        if (slnfFiles.Length == 0)
            return null;

        var slnfPath = slnfFiles[0];
        Console.WriteLine($"  Using package filter: {Path.GetFileName(slnfPath)}");

        try
        {
            var json = File.ReadAllText(slnfPath);
            var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
            var projectPaths = doc.RootElement
                .GetProperty("solution")
                .GetProperty("projects")
                .EnumerateArray()
                .Select(p => p.GetString())
                .Where(p => p != null)
                .Select(p => Path.GetFileNameWithoutExtension(p!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var packageMap = new Dictionary<string, List<string>>();
            foreach (var (name, _) in projects)
            {
                if (!projectPaths.Contains(name))
                    continue;

                if (!packageMap.TryGetValue(name, out var list))
                {
                    list = [];
                    packageMap[name] = list;
                }

                list.Add(name);
            }

            return packageMap;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Warning: Could not parse {Path.GetFileName(slnfPath)}: {ex.Message}");
            return null;
        }
    }

    private static Dictionary<string, List<string>> ResolveFromMsBuild(
        IReadOnlyList<(string Name, string FilePath)> projects)
    {
        var packageMap = new Dictionary<string, List<string>>();

        foreach (var (name, filePath) in projects)
        {
            try
            {
                var psi = new ProcessStartInfo("dotnet", ["msbuild", filePath, "-getProperty:IsPackable"])
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) continue;

                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(TimeSpan.FromSeconds(30));

                if (!string.Equals(output, "true", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!packageMap.TryGetValue(name, out var list))
                {
                    list = [];
                    packageMap[name] = list;
                }

                list.Add(name);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Warning: Could not evaluate {name}: {ex.Message}");
            }
        }

        return packageMap;
    }

    private static void PrintResults(Dictionary<string, List<string>> packageMap, int projectCount, string strategy)
    {
        Console.WriteLine($"  Packages: {packageMap.Count} (from {projectCount} projects, strategy: {strategy})");
        foreach (var (packageId, projectNames) in packageMap.OrderBy(kv => kv.Key))
        {
            if (projectNames.Count == 1 && projectNames[0] == packageId)
                Console.WriteLine($"    - {packageId}");
            else
                Console.WriteLine($"    - {packageId} ({string.Join(", ", projectNames)})");
        }
    }
}