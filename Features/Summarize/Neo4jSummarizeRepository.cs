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

    public async Task<List<IGraphNode>> GetTierNodesAsync(int tier, int? limit = null)
    {
        var limitClause = limit.HasValue ? $"LIMIT {limit.Value}" : "";
        var (records, _, _) = await driver
            .ExecutableQuery($@"
                MATCH (n) WHERE n.tier = $tier AND n.needsSummary = true
                  AND none(l IN labels(n) WHERE l STARTS WITH '_')
                WITH n {limitClause}
                CALL {{
                    WITH n
                    OPTIONAL MATCH (source)-[r]->(n)
                    RETURN collect(DISTINCT CASE WHEN source IS NOT NULL THEN {{
                        id: elementId(source), fullName: source.fullName,
                        summary: source.summary, relType: type(r), dir: 'in'
                    }} END) AS incoming
                }}
                CALL {{
                    WITH n
                    OPTIONAL MATCH (n)-[r]->(target)
                    RETURN collect(DISTINCT CASE WHEN target IS NOT NULL THEN {{
                        id: elementId(target), fullName: target.fullName,
                        summary: target.summary, relType: type(r), dir: 'out'
                    }} END) AS outgoing
                }}
                RETURN elementId(n) AS ElementId, n.fullName AS FullName,
                       labels(n) AS Labels, n.summary AS OldSummary, n.searchText AS SearchText,
                       n.sourceText AS SourceText, n.returnType AS ReturnType,
                       n.parameters AS Parameters, n.members AS Members, n.bodyHash AS BodyHash,
                       incoming, outgoing")
            .WithParameters(new { tier })
            .ExecuteAsync();

        return records.Select(MapToTypedNode).ToList();
    }

    public async Task<IGraphNode?> GetNodeByIdAsync(string elementId)
    {
        var (records, _, _) = await driver
            .ExecutableQuery(@"
                MATCH (n) WHERE elementId(n) = $elementId
                WITH n
                CALL {
                    WITH n
                    OPTIONAL MATCH (source)-[r]->(n)
                    RETURN collect(DISTINCT CASE WHEN source IS NOT NULL THEN {
                        id: elementId(source), fullName: source.fullName,
                        summary: source.summary, relType: type(r), dir: 'in'
                    } END) AS incoming
                }
                CALL {
                    WITH n
                    OPTIONAL MATCH (n)-[r]->(target)
                    RETURN collect(DISTINCT CASE WHEN target IS NOT NULL THEN {
                        id: elementId(target), fullName: target.fullName,
                        summary: target.summary, relType: type(r), dir: 'out'
                    } END) AS outgoing
                }
                RETURN elementId(n) AS ElementId, n.fullName AS FullName,
                       labels(n) AS Labels, n.summary AS OldSummary, n.searchText AS SearchText,
                       n.sourceText AS SourceText, n.returnType AS ReturnType,
                       n.parameters AS Parameters, n.members AS Members, n.bodyHash AS BodyHash,
                       incoming, outgoing")
            .WithParameters(new { elementId })
            .ExecuteAsync();

        var r = records.FirstOrDefault();
        return r == null ? null : MapToTypedNode(r);
    }

    public async Task<List<TierInfo>> GetTierBreakdownAsync()
    {
        var results = await driver
            .ExecutableQuery(@"
                MATCH (n) WHERE n.tier IS NOT NULL
                WITH n.tier AS tier, labels(n) AS nodeLabels, n.needsSummary AS pending
                UNWIND nodeLabels AS label
                RETURN tier AS Tier, label AS Label, count(*) AS Total,
                       sum(CASE WHEN pending = true THEN 1 ELSE 0 END) AS Pending
                ORDER BY Tier, Label")
            .ExecuteAsync()
            .AsObjectsAsync<TierInfo>();

        return results.Where(t => NodeType.All.Contains(t.Label)).ToList();
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

    public async Task<Dictionary<string, int>> GetAllTagsWithCountsAsync()
    {
        var results = await driver
            .ExecutableQuery(@"
                MATCH (n) WHERE n.tags IS NOT NULL
                UNWIND n.tags AS tag
                RETURN tag, count(*) AS count
                ORDER BY count DESC")
            .ExecuteAsync()
            .AsObjectsAsync((string tag, long count) => (tag, (int)count));

        return results.ToDictionary(r => r.tag, r => r.Item2);
    }

    public async Task<int> RemapTagsAsync(Dictionary<string, string> mapping)
    {
        var updated = 0;
        foreach (var (oldTag, newTag) in mapping)
        {
            if (oldTag == newTag) continue;
            var (records, _, _) = await driver
                .ExecutableQuery(@"
                    MATCH (n) WHERE $oldTag IN n.tags
                    SET n.tags = [t IN n.tags | CASE WHEN t = $oldTag THEN $newTag ELSE t END]
                    RETURN count(n) AS cnt")
                .WithParameters(new { oldTag, newTag })
                .ExecuteAsync();
            updated += records.Single()["cnt"].As<int>();
        }
        return updated;
    }

    // --- Mapping ---

    private record RawEdge(string Id, string FullName, string? Summary, string RelType, string Dir);

    private static IGraphNode MapToTypedNode(IRecord r)
    {
        var labels = r["Labels"].As<List<string>>();
        var incoming = MapEdges(r["incoming"]);
        var outgoing = MapEdges(r["outgoing"]);
        var nodeType = labels.FirstOrDefault(NodeType.All.Contains);

        var id = new NodeId(r["ElementId"].As<string>());
        var fullName = r["FullName"].As<string>();
        var summary = r["OldSummary"]?.As<string>();
        var searchText = r["SearchText"]?.As<string>();
        var bodyHash = r["BodyHash"]?.As<string>();
        var sourceText = r["SourceText"]?.As<string>();

        return nodeType switch
        {
            NodeType.Method => new MethodNode(id, fullName, labels, summary, searchText, bodyHash,
                sourceText, r["ReturnType"]?.As<string>(), r["Parameters"]?.As<string>(),
                Calls: ToRelated(incoming, RelType.CalledBy),
                CalledBy: ToRelated(outgoing, RelType.CalledBy),
                Implements: ToRelated(incoming, RelType.ImplementsMethod),
                DefinedIn: ToSingle(outgoing, RelType.DefinedBy),
                References: ToRelated(incoming, RelType.ReferencedBy),
                Registers: ToRelated(incoming, RelType.RegisteredBy),
                Extends: ToRelated(outgoing, RelType.Extends)),

            NodeType.Class => new ClassNode(id, fullName, labels, summary, searchText, bodyHash, sourceText,
                Members: ToRelated(incoming, RelType.DefinedBy),
                ReferencedBy: ToRelated(outgoing, RelType.ReferencedBy),
                RegisteredBy: ToRelated(outgoing, RelType.RegisteredBy),
                ExtendedBy: ToRelated(incoming, RelType.Extends),
                Implements: ToRelated(outgoing, RelType.Implements),
                Namespace: ToSingle(outgoing, RelType.BelongsToNamespace)),

            NodeType.Interface => new InterfaceNode(id, fullName, labels, summary, searchText, bodyHash, sourceText,
                Members: ToRelated(incoming, RelType.DefinedBy),
                ImplementedBy: ToRelated(incoming, RelType.Implements),
                ReferencedBy: ToRelated(outgoing, RelType.ReferencedBy),
                RegisteredBy: ToRelated(outgoing, RelType.RegisteredBy),
                Namespace: ToSingle(outgoing, RelType.BelongsToNamespace)),

            NodeType.Enum => new EnumNode(id, fullName, labels, summary, searchText, bodyHash,
                sourceText, r["Members"]?.As<string>(),
                ReferencedBy: ToRelated(outgoing, RelType.ReferencedBy),
                Namespace: ToSingle(outgoing, RelType.BelongsToNamespace)),

            NodeType.Namespace => new NamespaceNode(id, fullName, labels, summary, searchText, bodyHash,
                Types: ToRelated(incoming, RelType.BelongsToNamespace),
                Project: ToSingle(outgoing, RelType.BelongsToProject)),

            NodeType.Project => new ProjectNode(id, fullName, labels, summary, searchText, bodyHash,
                Namespaces: ToRelated(incoming, RelType.BelongsToProject),
                Solution: ToSingle(outgoing, RelType.BelongsToSolution)),

            NodeType.Package => new PackageNode(id, fullName, labels, summary, searchText, bodyHash,
                Projects: ToRelated(incoming, RelType.BelongsToPackage)),

            NodeType.Solution => new SolutionNode(id, fullName, labels, summary, searchText, bodyHash,
                Projects: ToRelated(incoming, RelType.BelongsToSolution)),

            _ => throw new InvalidOperationException($"Unknown node type: {nodeType}")
        };
    }

    private static List<RawEdge> MapEdges(object? raw)
    {
        if (raw is not List<object?> list) return [];
        return list
            .OfType<IDictionary<string, object>>()
            .Select(d => new RawEdge(
                Id: d["id"]?.ToString() ?? "",
                FullName: d["fullName"]?.ToString() ?? "",
                Summary: d["summary"]?.ToString(),
                RelType: d["relType"]?.ToString() ?? "",
                Dir: d["dir"]?.ToString() ?? ""))
            .ToList();
    }

    private static List<RelatedNode> ToRelated(List<RawEdge> edges, string relType) =>
        edges.Where(e => e.RelType == relType)
            .Select(e => new RelatedNode(new NodeId(e.Id), e.FullName, e.Summary)).ToList();

    private static RelatedNode? ToSingle(List<RawEdge> edges, string relType) =>
        edges.Where(e => e.RelType == relType)
            .Select(e => new RelatedNode(new NodeId(e.Id), e.FullName, e.Summary))
            .FirstOrDefault();
}

public record TierInfo(int Tier, string Label, int Total, int Pending);
