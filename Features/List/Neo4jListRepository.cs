using Neo4j.Driver;
using Neo4j.Driver.Mapping;

namespace GraphRagCli.Features.List;

public class Neo4jListRepository(IDriver driver)
{
    public async Task<DatabaseInfo> GetDatabaseInfoAsync()
    {
        var counts = await driver
            .ExecutableQuery(@"
                MATCH (n)
                WITH [l IN labels(n) WHERE l IN ['Project','Namespace','Class','Interface','Method','Enum']][0] AS label
                WHERE label IS NOT NULL
                RETURN label, count(*) AS count
                ORDER BY count DESC")
            .ExecuteAsync()
            .AsObjectsAsync((string label, long count) => (label, count));

        var projects = await driver
            .ExecutableQuery(@"
                MATCH (p:Project)
                OPTIONAL MATCH (ns:Namespace)-[:BELONGS_TO_PROJECT]->(p)<--(member)
                WITH p, count(DISTINCT member) AS MemberCount
                RETURN p.fullName AS Name,
                       coalesce(p.searchText, p.summary) AS Summary,
                       MemberCount
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
            .ExecutableQuery(@"
                MATCH (n)
                WHERE any(l IN labels(n) WHERE l IN ['Class','Interface','Method','Enum'])
                RETURN count(n) AS total, count(n.embedding) AS embedded")
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
