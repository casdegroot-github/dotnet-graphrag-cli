namespace GraphRagCli.Features.Ingest.Analysis;

public class SolutionResolver
{
    public string? ResolveSolutionPath(string inputPath)
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
            var slnx = Directory.GetFiles(inputPath, "*.slnx", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (slnx != null) return Path.GetFullPath(slnx);

            var sln = Directory.GetFiles(inputPath, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (sln != null) return Path.GetFullPath(sln);

            slnx = Directory.GetFiles(inputPath, "*.slnx", SearchOption.AllDirectories).FirstOrDefault();
            if (slnx != null) return Path.GetFullPath(slnx);

            sln = Directory.GetFiles(inputPath, "*.sln", SearchOption.AllDirectories).FirstOrDefault();
            if (sln != null) return Path.GetFullPath(sln);

            var csproj = Directory.GetFiles(inputPath, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (csproj != null) return Path.GetFullPath(csproj);

            csproj = Directory.GetFiles(inputPath, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
            if (csproj != null) return Path.GetFullPath(csproj);
        }

        return null;
    }
}
