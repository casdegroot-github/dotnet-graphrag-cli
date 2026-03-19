using GraphRagCli.Features.Ingest.Analysis;
using GraphRagCli.Features.Ingest.GraphDb;
using Neo4j.Driver;

namespace GraphRagCli.Features.Ingest;

public class IngestService(
    ICodeAnalyzer codeAnalyzer,
    PackageResolver packageResolver)
{
    public async Task<IngestResult> IngestAsync(IDriver driver, string solutionPath, IngestParams parameters)
    {
        var analysis = await codeAnalyzer.AnalyzeSolutionAsync(
            solutionPath, parameters.SkipTests, parameters.SkipSamples);

        if (analysis.Results.Count == 0)
            return IngestResult.Empty;

        var solutionName = Path.GetFileNameWithoutExtension(solutionPath);
        var runTimestamp = DateTime.UtcNow;
        var repo = new Neo4jIngestRepository(driver);
        var postProcessor = new Neo4jIngestPostProcessor(driver);

        var projectStats = await IngestAsync(repo, solutionName, analysis.Results, runTimestamp);

        var packageMap = packageResolver.ResolvePackages(solutionPath, analysis.ProjectFilePaths);
        if (packageMap.Count > 0)
            await repo.IngestPackageNodesAsync(packageMap, runTimestamp);

        var nugetProjects = packageMap.Count > 0
            ? packageMap.Values.SelectMany(p => p).ToList()
            : null;

        var reconcileResult = await ReconcileAsync(postProcessor, runTimestamp);
        var labelResult = await LabelAsync(postProcessor, nugetProjects);
        await postProcessor.ComputeTiersAsync();

        return new IngestResult(
            solutionName, projectStats, reconcileResult,
            labelResult.EntryPoints, labelResult.PublicApi, packageMap.Count);
    }

    private static async Task<List<ProjectIngestStats>> IngestAsync(
        Neo4jIngestRepository repo, string solutionName,
        Dictionary<string, AnalysisResult> analysisResults, DateTime runTimestamp)
    {
        var projectStats = new List<ProjectIngestStats>();

        foreach (var (name, r) in analysisResults)
        {
            // Nodes
            await repo.IngestProjectNodeAsync(name, runTimestamp);
            await repo.IngestNamespaceNodesAsync(name, r.Namespaces, runTimestamp);
            await repo.IngestClassNodesAsync(r.Classes, runTimestamp);
            await repo.IngestInterfaceNodesAsync(r.Interfaces, runTimestamp);
            await repo.IngestEnumNodesAsync(r.Enums, runTimestamp);
            await repo.IngestMethodNodesAsync(name, r.Methods, r.Calls, runTimestamp);

            // Edges
            await repo.IngestDefinedByEdgesAsync(r.Methods, runTimestamp);
            await repo.IngestInheritanceEdgesAsync(r.Classes, runTimestamp);
            await repo.IngestImplementsEdgesAsync(r.Classes, runTimestamp);
            await repo.IngestInterfaceInheritanceEdgesAsync(r.Interfaces, runTimestamp);
            await repo.IngestNamespaceMembershipEdgesAsync(r.Classes, r.Interfaces, r.Enums, runTimestamp);
            await repo.IngestExtensionMethodEdgesAsync(r.Methods, runTimestamp);
            await repo.IngestCalledByEdgesAsync(r.Calls, runTimestamp);
            await repo.IngestRegistrationEdgesAsync(r.References, runTimestamp);
            await repo.IngestReferenceEdgesAsync(r.References, runTimestamp);

            projectStats.Add(new ProjectIngestStats(name, r.Namespaces.Count, r.Classes.Count,
                r.Interfaces.Count, r.Methods.Count, r.Enums.Count, r.Calls.Count, r.References.Count));
        }

        await repo.IngestSolutionNodeAsync(solutionName, analysisResults.Keys);

        return projectStats;
    }

    private static async Task<ReconcileResult> ReconcileAsync(Neo4jIngestPostProcessor postProcessor, DateTime runTimestamp)
    {
        var transferred = await postProcessor.TransferByBodyHashAsync(runTimestamp);
        var staleEdges = await postProcessor.DeleteStaleEdgesAsync(runTimestamp);
        var staleNodes = await postProcessor.DeleteStaleNodesAsync(runTimestamp);
        await postProcessor.MarkStaleDependentsAsync(runTimestamp);
        return new ReconcileResult(transferred, staleEdges, staleNodes);
    }

    private static async Task<LabelResult> LabelAsync(
        Neo4jIngestPostProcessor postProcessor, List<string>? nugetProjects)
    {
        await postProcessor.LabelEmbeddableNodesAsync();
        var entryPointResult = await postProcessor.LabelEntryPointsAsync();
        var publicApiResult = await postProcessor.LabelPublicApiAsync(nugetProjects);

        return new LabelResult(entryPointResult, publicApiResult);
    }

    private record LabelResult(EntryPointResult EntryPoints, PublicApiResult PublicApi);
}

public record IngestResult(
    string SolutionName,
    List<ProjectIngestStats> Projects,
    ReconcileResult Reconcile,
    EntryPointResult EntryPoints,
    PublicApiResult PublicApi,
    int PackageCount)
{
    public static readonly IngestResult Empty = new(
        "", [], new ReconcileResult(0, 0, 0), new EntryPointResult(0, 0),
        new PublicApiResult(new Dictionary<string, long>(), 0, 0), 0);

    public bool IsEmpty => Projects.Count == 0;
}

public record ProjectIngestStats(
    string Name, int Namespaces, int Classes, int Interfaces,
    int Methods, int Enums, int Calls, int References);
