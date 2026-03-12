namespace GraphRagCli.Features.Ingest.Analysis;

public interface ICodeAnalyzer
{
    Task<Dictionary<string, AnalysisResult>> AnalyzeSolutionAsync(
        string solutionPath, bool skipTests, bool skipSamples);
}
