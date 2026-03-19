namespace GraphRagCli.Features.Ingest.Analysis;

public interface ICodeAnalyzer
{
    Task<SolutionAnalysis> AnalyzeSolutionAsync(
        string solutionPath, bool skipTests, bool skipSamples);
}

public record SolutionAnalysis(
    Dictionary<string, AnalysisResult> Results,
    List<(string Name, string FilePath)> ProjectFilePaths);
