using GraphRagCli.Shared;
using Neo4j.Driver;
using Neo4j.Driver.Mapping;

namespace GraphRagCli.Features.List;

public class Neo4jListRepository(IDriver driver)
{
    public async Task<DatabaseInfo> GetDatabaseInfoAsync()
    {
        var allCounts = await driver
            .ExecutableQuery(@"
                MATCH (n)
                UNWIND labels(n) AS label
                RETURN label, count(*) AS count
                ORDER BY count DESC")
            .ExecuteAsync()
            .AsObjectsAsync((string label, long count) => (label, count));

        var counts = allCounts.Where(c => NodeType.All.Contains(c.label)).ToList();

        var projects = await driver
            .ExecutableQuery($@"
                MATCH (p:Project)
                OPTIONAL MATCH (ns:Namespace)-[:{RelType.BelongsToProject}]->(p)
                OPTIONAL MATCH (member)-[:{RelType.BelongsToNamespace}]->(ns)
                WITH p, count(DISTINCT member) AS MemberCount
                RETURN p.fullName AS Name,
                       coalesce(p.summary, p.searchText, '') AS Summary,
                       coalesce(MemberCount, 0) AS MemberCount
                ORDER BY p.fullName")
            .ExecuteAsync()
            .AsObjectsAsync<ProjectInfo>();

        var solutions = await driver
            .ExecutableQuery(@"
                MATCH (s:Solution)
                RETURN s.fullName AS Name,
                       coalesce(s.searchText, s.summary) AS Summary
                ORDER BY s.fullName")
            .ExecuteAsync()
            .AsObjectsAsync<SolutionInfo>();

        var embedStats = await driver
            .ExecutableQuery($"MATCH (n:{NodeLabels.Embeddable}) RETURN count(n) AS total, count(n.embedding) AS embedded")
            .ExecuteAsync()
            .AsObjectsAsync((long total, long embedded) => (total, embedded));

        var (total, embedded) = embedStats.Single();

        return new DatabaseInfo(
            solutions.ToList(),
            counts.ToDictionary(c => c.label, c => c.count),
            projects.ToList(),
            total,
            embedded);
    }
}
