using GraphRagCli.Shared;
using Neo4j.Driver;

namespace GraphRagCli.Features.Database;

/// <summary>
/// One-time schema setup: constraints and indexes.
/// Called during database init, not during ingest/embed.
/// </summary>
public static class Neo4jSchemaService
{
    public static async Task InitializeAsync(IDriver driver)
    {
        // Node uniqueness constraints
        await driver.ExecutableQuery($"CREATE CONSTRAINT IF NOT EXISTS FOR (s:{NodeLabels.Solution}) REQUIRE s.fullName IS UNIQUE").ExecuteAsync();
        await driver.ExecutableQuery($"CREATE CONSTRAINT IF NOT EXISTS FOR (p:{NodeLabels.Project}) REQUIRE p.fullName IS UNIQUE").ExecuteAsync();
        await driver.ExecutableQuery($"CREATE CONSTRAINT IF NOT EXISTS FOR (c:{NodeLabels.Class}) REQUIRE c.fullName IS UNIQUE").ExecuteAsync();
        await driver.ExecutableQuery($"CREATE CONSTRAINT IF NOT EXISTS FOR (i:{NodeLabels.Interface}) REQUIRE i.fullName IS UNIQUE").ExecuteAsync();
        await driver.ExecutableQuery($"CREATE CONSTRAINT IF NOT EXISTS FOR (m:{NodeLabels.Method}) REQUIRE m.fullName IS UNIQUE").ExecuteAsync();
        await driver.ExecutableQuery($"CREATE CONSTRAINT IF NOT EXISTS FOR (n:{NodeLabels.Namespace}) REQUIRE n.fullName IS UNIQUE").ExecuteAsync();
        await driver.ExecutableQuery($"CREATE CONSTRAINT IF NOT EXISTS FOR (e:{NodeLabels.Enum}) REQUIRE e.fullName IS UNIQUE").ExecuteAsync();

        // Vector index is created by the embed command (dimensions depend on model)

        // Fulltext index for keyword search
        await driver.ExecutableQuery($"""
            CREATE FULLTEXT INDEX embeddable_fulltext IF NOT EXISTS
            FOR (n:{NodeLabels.Embeddable}) ON EACH [n.name, n.fullName, n.searchText]
            """).ExecuteAsync();

        Console.WriteLine("Schema initialized (constraints + indexes).");
    }
}
