using GraphRagCli.Shared;
using Neo4j.Driver;
using Neo4j.Driver.Mapping;

namespace GraphRagCli.Features.Summarize;

public class Neo4jSummarizeRepository(IDriver driver)
{
    public async Task<int> GetMaxDepthAsync()
    {
        var results = await driver
            .ExecutableQuery("MATCH (n) WHERE n.tier IS NOT NULL RETURN COALESCE(max(n.tier), 0) AS maxTier")
            .ExecuteAsync()
            .AsObjectsAsync((int maxTier) => maxTier);
        return results.Single();
    }

    public async Task<int> MarkAllNeedsSummaryAsync()
    {
        var (_, summary, _) = await driver
            .ExecutableQuery("MATCH (n) WHERE n.tier IS NOT NULL SET n.needsSummary = true RETURN count(n) AS cnt")
            .ExecuteAsync();
        return summary.Counters.PropertiesSet;
    }

    public async Task<List<ReadyNodeData>> GetTierNodesAsync(int tier, int? limit = null)
    {
        var limitClause = limit.HasValue ? $"LIMIT {limit.Value}" : "";
        var (records, _, _) = await driver
            .ExecutableQuery($@"
                MATCH (n) WHERE n.tier = $tier AND n.needsSummary = true
                WITH n {limitClause}
                OPTIONAL MATCH (child)-->(n)
                RETURN elementId(n) AS ElementId, n.fullName AS FullName,
                       labels(n) AS Labels,
                       n.sourceText AS SourceText, n.returnType AS ReturnType,
                       n.parameters AS Parameters, n.members AS Members, n.bodyHash AS BodyHash,
                       collect(DISTINCT CASE WHEN child IS NOT NULL THEN {{
                           name: child.name, fullName: child.fullName,
                           summary: child.summary, sourceText: child.sourceText,
                           labels: labels(child)
                       }} END) AS Children,
                       sum(CASE WHEN child IS NOT NULL AND child.summary IS NULL THEN 1 ELSE 0 END) AS MissingChildSummaries")
            .WithParameters(new { tier })
            .ExecuteAsync();

        return records.Select(r => new ReadyNodeData(
            ElementId: r["ElementId"].As<string>(),
            FullName: r["FullName"].As<string>(),
            Labels: r["Labels"].As<List<string>>(),
            SourceText: r["SourceText"]?.As<string>(),
            ReturnType: r["ReturnType"]?.As<string>(),
            Parameters: r["Parameters"]?.As<string>(),
            Members: r["Members"]?.As<string>(),
            BodyHash: r["BodyHash"]?.As<string>(),
            Children: MapChildren(r["Children"].As<List<IDictionary<string, object>?>>()),
            MissingChildSummaries: r["MissingChildSummaries"].As<int>()
        )).ToList();
    }

    private static List<ChildData> MapChildren(List<IDictionary<string, object>?> raw) =>
        raw.OfType<IDictionary<string, object>>()
            .Select(c => new ChildData(
                Name: (c["name"] as string) ?? (c["fullName"] as string) ?? "",
                FullName: (c["fullName"] as string) ?? "",
                Summary: c["summary"] as string,
                SourceText: c["sourceText"] as string,
                Labels: (c["labels"] as IEnumerable<object>)?.Select(l => l.ToString()!).ToList() ?? []))
            .Where(c => c.Name.Length > 0)
            .ToList();

    public async Task<List<TierInfo>> GetTierBreakdownAsync()
    {
        return await driver
            .ExecutableQuery(@"
                MATCH (n) WHERE n.tier IS NOT NULL
                WITH n.tier AS tier, labels(n) AS nodeLabels, n.needsSummary AS pending
                UNWIND nodeLabels AS label
                WITH tier, label, pending
                WHERE label IN ['Method', 'Class', 'Interface', 'Enum', 'Namespace', 'Project', 'Solution']
                RETURN tier AS Tier, label AS Label, count(*) AS Total,
                       sum(CASE WHEN pending = true THEN 1 ELSE 0 END) AS Pending
                ORDER BY Tier, Label")
            .ExecuteAsync()
            .AsObjectsAsync<TierInfo>()
            .ContinueWith(t => t.Result.ToList());
    }

    public async Task SetSummariesBatchAsync(List<(string ElementId, string Summary, string? SearchText, string[] Tags)> batch)
    {
        foreach (var chunk in batch.Chunk(50))
        {
            await driver
                .ExecutableQuery($"""
                    UNWIND $batch AS item
                    MATCH (n) WHERE elementId(n) = item.elementId
                    SET n.summary = item.summary,
                        n.searchText = item.searchText,
                        n.tags = item.tags,
                        n.needsSummary = false
                    WITH n WHERE n:{NodeLabels.Embeddable}
                    SET n.needsEmbedding = true
                    """)
                .WithParameters(new
                {
                    batch = chunk.Select(x => new
                    {
                        elementId = x.ElementId,
                        summary = x.Summary,
                        searchText = x.SearchText ?? x.Summary,
                        tags = x.Tags.ToList()
                    }).ToList()
                })
                .ExecuteAsync();

            // Propagate needsSummary one level up to all direct parents
            await driver
                .ExecutableQuery(@"
                    UNWIND $elementIds AS eid
                    MATCH (n) WHERE elementId(n) = eid
                    MATCH (n)-->(parent)
                    SET parent.needsSummary = true")
                .WithParameters(new { elementIds = chunk.Select(x => x.ElementId).ToList() })
                .ExecuteAsync();
        }
    }

}

public record TierInfo(int Tier, string Label, int Total, int Pending);
