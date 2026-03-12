using GraphRagCli.Shared.GraphDb;
using Neo4j.Driver;

namespace GraphRagCli.Features.List;

public class Neo4jListRepository(IDriver driver)
{
    public async Task<DatabaseInfo> GetDatabaseInfoAsync()
    {
        var (countRecords, _, _) = await driver
            .ExecutableQuery(@"
                MATCH (n)
                WITH [l IN labels(n) WHERE l IN ['Project','Namespace','Class','Interface','Method','Enum']][0] AS label
                WHERE label IS NOT NULL
                RETURN label, count(*) AS count
                ORDER BY count DESC")
            .ExecuteAsync();

        var counts = countRecords.ToDictionary(r => r["label"].As<string>(), r => r["count"].As<long>());

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
            .MapAsync<ProjectInfo>();

        var solutions = await driver
            .ExecutableQuery(@"
                MATCH (s:Solution)
                RETURN s.fullName AS Name,
                       coalesce(s.searchText, s.summary) AS Summary
                ORDER BY s.fullName")
            .ExecuteAsync()
            .MapAsync<SolutionInfo>();

        var (embedRecords, _, _) = await driver
            .ExecutableQuery(@"
                MATCH (n)
                WHERE any(l IN labels(n) WHERE l IN ['Class','Interface','Method','Enum'])
                RETURN count(n) AS total, count(n.embedding) AS embedded")
            .ExecuteAsync();

        var embedRow = embedRecords.Single();

        return new DatabaseInfo(
            solutions.ToList(),
            counts,
            projects.ToList(),
            embedRow["total"].As<long>(),
            embedRow["embedded"].As<long>());
    }
}