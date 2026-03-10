using System.Text.Json;

namespace GraphRagCli.Helpers;

public static class SolutionHelper
{
    public static string? ResolveSolutionPath(string inputPath)
    {
        if (File.Exists(inputPath))
        {
            if (inputPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                inputPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) ||
                inputPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(inputPath);
        }

        if (Directory.Exists(inputPath))
        {
            // Prefer .slnx > .sln > .csproj
            var slnx = Directory.GetFiles(inputPath, "*.slnx", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (slnx != null) return Path.GetFullPath(slnx);

            var sln = Directory.GetFiles(inputPath, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (sln != null) return Path.GetFullPath(sln);

            slnx = Directory.GetFiles(inputPath, "*.slnx", SearchOption.AllDirectories).FirstOrDefault();
            if (slnx != null) return Path.GetFullPath(slnx);

            sln = Directory.GetFiles(inputPath, "*.sln", SearchOption.AllDirectories).FirstOrDefault();
            if (sln != null) return Path.GetFullPath(sln);

            // Fall back to .csproj
            var csproj = Directory.GetFiles(inputPath, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (csproj != null) return Path.GetFullPath(csproj);

            csproj = Directory.GetFiles(inputPath, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
            if (csproj != null) return Path.GetFullPath(csproj);
        }

        return null;
    }

    public static List<string>? ResolveNuGetProjects(string? explicitSlnfPath, string solutionPath)
    {
        string? slnfPath = explicitSlnfPath;
        if (slnfPath == null)
        {
            var solutionDir = Path.GetDirectoryName(solutionPath)!;
            slnfPath = Directory.GetFiles(solutionDir, "*.NuGet.slnf", SearchOption.TopDirectoryOnly).FirstOrDefault()
                    ?? Directory.GetFiles(solutionDir, "*.slnf", SearchOption.TopDirectoryOnly).FirstOrDefault();
        }

        if (slnfPath == null || !File.Exists(slnfPath)) return null;

        var json = JsonDocument.Parse(File.ReadAllText(slnfPath));
        var projects = json.RootElement
            .GetProperty("solution")
            .GetProperty("projects")
            .EnumerateArray()
            .Select(p => Path.GetFileNameWithoutExtension(p.GetString()!))
            .ToList();

        Console.WriteLine($"  NuGet .slnf: {slnfPath}");
        foreach (var p in projects)
            Console.WriteLine($"    - {p}");

        return projects;
    }
}
