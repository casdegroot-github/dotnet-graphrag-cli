using GraphRagCli.Shared.GraphDb;
using Neo4j.Driver;
using Neo4j.Driver.Mapping;

namespace GraphRagCli.Features.Summarize;

public class Neo4jSummarizeRepository(IDriver driver)
{
    public async Task<List<RawNodeData>> GetLeafRawNodesAsync(bool force)
    {
        return await driver
            .ExecutableQuery(@"
                MATCH (n:Embeddable)
                WHERE NOT exists { (n)-[]->(other:Embeddable) }
                  AND (n.needsSummary = true OR $force)
                RETURN elementId(n) AS ElementId, n.fullName AS FullName,
                       labels(n) AS Labels,
                       n.sourceText AS SourceText,
                       n.returnType AS ReturnType,
                       n.parameters AS Parameters,
                       n.members AS Members,
                       n.bodyHash AS BodyHash")
            .WithParameters(new { force })
            .ExecuteAsync()
            .MapAsync<RawNodeData>();
    }

    public async Task<Dictionary<int, List<string>>> GetContextualTiersAsync(bool force)
    {
        var (records, _, _) = await driver
            .ExecutableQuery(@"
                MATCH (n:Embeddable)
                WHERE exists { (n)-[]->(other:Embeddable) }
                  AND (n.needsSummary = true OR $force)
                OPTIONAL MATCH path = (n)-[*1..20]->(dep:Embeddable)
                WHERE exists { (dep)-[]->(dep2:Embeddable) }
                WITH n, CASE WHEN path IS NULL THEN 0 ELSE length(path) END AS depth
                WITH elementId(n) AS elementId, max(depth) AS tier
                RETURN elementId, tier ORDER BY tier")
            .WithParameters(new { force })
            .ExecuteAsync();

        var tiers = new Dictionary<int, List<string>>();
        foreach (var r in records)
        {
            var tier = r["tier"].As<int>();
            if (!tiers.TryGetValue(tier, out var list))
            {
                list = [];
                tiers[tier] = list;
            }
            list.Add(r["elementId"].As<string>());
        }

        return tiers;
    }

    public async Task<List<RawContextualNodeData>> GetContextualRawNodesAsync(
        bool force, IReadOnlyList<string>? elementIds = null)
    {
        var (records, _, _) = await driver
            .ExecutableQuery(@"
                MATCH (n:Embeddable)
                WHERE exists { (n)-[]->(other:Embeddable) }
                AND (n.needsSummary = true OR $force)
                AND (elementId(n) IN $elementIds OR $elementIds IS NULL)
                CALL { WITH n
                    OPTIONAL MATCH (n)-[r]-(neighbor:Embeddable)
                    RETURN collect(DISTINCT {
                        name: neighbor.name,
                        fullName: neighbor.fullName,
                        summary: neighbor.summary,
                        sourceText: neighbor.sourceText,
                        rel: type(r),
                        labels: labels(neighbor),
                        isEntryPoint: neighbor:EntryPoint
                    }) AS neighbors
                }
                RETURN elementId(n) AS ElementId, n.fullName AS FullName,
                       labels(n) AS Labels,
                       n.sourceText AS SourceText,
                       n.returnType AS ReturnType,
                       n.parameters AS Parameters,
                       n.members AS Members,
                       n.bodyHash AS BodyHash,
                       neighbors")
            .WithParameters(new { force, elementIds = elementIds?.ToList() })
            .ExecuteAsync();

        var nodes = new List<RawContextualNodeData>();
        foreach (var r in records)
        {
            var rawNode = r.Map<RawNodeData>();

            var rawNeighbors = r["neighbors"].As<List<IDictionary<string, object>?>>();
            var neighbors = rawNeighbors
                .Where(n => n != null)
                .Select(n => new NeighborData(
                    Name: n!["name"]?.ToString() ?? "",
                    FullName: n["fullName"]?.ToString() ?? "",
                    Summary: n["summary"]?.ToString() ?? "",
                    SourceText: n["sourceText"]?.ToString(),
                    Rel: n["rel"]?.ToString() ?? "",
                    Labels: (n["labels"] as IEnumerable<object>)?.Select(l => l.ToString()!).ToList() ?? [],
                    IsEntryPoint: n["isEntryPoint"] is true))
                .Where(n => n.Name.Length > 0)
                .ToList();

            nodes.Add(new RawContextualNodeData(rawNode, neighbors));
        }

        return nodes;
    }

    /// <summary>
    /// Relationship-agnostic aggregation query. Finds all children of the given parent label,
    /// filters to roots only (children not depended on by siblings — harmless no-op when children
    /// have no inter-sibling edges, e.g. Project→Namespace or Solution→Project).
    /// </summary>
    public async Task<List<AggregationData>> GetAggregationChildrenAsync(string parentLabel, bool force)
    {
        return await driver
            .ExecutableQuery($@"
                MATCH (parent:{parentLabel})--(child)
                WHERE child.summary IS NOT NULL
                  AND (COALESCE(parent.needsSummary, true) OR $force)
                WITH parent, collect(child) AS members
                WITH parent, [m IN members WHERE NOT EXISTS {{
                    MATCH (other)-->(m) WHERE other IN members AND other <> m
                }}] AS roots
                UNWIND roots AS child
                RETURN elementId(parent) AS ElementId, parent.fullName AS FullName,
                       collect(child.fullName + ': ' + child.summary) AS ChildSummaries
                ORDER BY parent.fullName")
            .WithParameters(new { force })
            .ExecuteAsync()
            .MapAsync<AggregationData>();
    }

    public async Task SetSummariesBatchAsync(List<(string ElementId, string Summary, string? SearchText, string[] Tags)> batch)
    {
        foreach (var chunk in batch.Chunk(50))
        {
            await driver
                .ExecutableQuery(@"
                    UNWIND $batch AS item
                    MATCH (n) WHERE elementId(n) = item.elementId
                    SET n.summary = item.summary,
                        n.searchText = item.searchText,
                        n.tags = item.tags,
                        n.needsSummary = false
                    WITH n WHERE n:Embeddable
                    SET n.needsEmbedding = true")
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

            // Propagate needsSummary one level up: Embeddable → Namespace → Project → Solution
            await driver
                .ExecutableQuery(@"
                    UNWIND $elementIds AS eid
                    MATCH (n) WHERE elementId(n) = eid
                    MATCH (n)-->(parent)
                    WHERE parent:Namespace OR parent:Project OR parent:Solution
                    SET parent.needsSummary = true")
                .WithParameters(new { elementIds = chunk.Select(x => x.ElementId).ToList() })
                .ExecuteAsync();
        }
    }

    public async Task<int> MarkStaleDependentsAsync(List<string> changedFullNames)
    {
        if (changedFullNames.Count == 0) return 0;

        var results = await driver
            .ExecutableQuery(@"
                UNWIND $names AS name
                MATCH (changed {fullName: name})
                OPTIONAL MATCH (dependent)-[:CALLS|DEFINES|IMPLEMENTS|REFERENCES|EXTENDS|INHERITS_FROM]-(changed)
                WHERE dependent:Embeddable
                SET dependent.needsSummary = true
                RETURN count(DISTINCT dependent)")
            .WithParameters(new { names = changedFullNames })
            .ExecuteAsync()
            .MapAsync<int>();

        return results.Single();
    }
}