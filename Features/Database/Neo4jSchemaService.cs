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
        await driver.ExecutableQuery("CREATE CONSTRAINT IF NOT EXISTS FOR (s:Solution) REQUIRE s.fullName IS UNIQUE").ExecuteAsync();
        await driver.ExecutableQuery("CREATE CONSTRAINT IF NOT EXISTS FOR (p:Project) REQUIRE p.fullName IS UNIQUE").ExecuteAsync();
        await driver.ExecutableQuery("CREATE CONSTRAINT IF NOT EXISTS FOR (c:Class) REQUIRE c.fullName IS UNIQUE").ExecuteAsync();
        await driver.ExecutableQuery("CREATE CONSTRAINT IF NOT EXISTS FOR (i:Interface) REQUIRE i.fullName IS UNIQUE").ExecuteAsync();
        await driver.ExecutableQuery("CREATE CONSTRAINT IF NOT EXISTS FOR (m:Method) REQUIRE m.fullName IS UNIQUE").ExecuteAsync();
        await driver.ExecutableQuery("CREATE CONSTRAINT IF NOT EXISTS FOR (n:Namespace) REQUIRE n.fullName IS UNIQUE").ExecuteAsync();
        await driver.ExecutableQuery("CREATE CONSTRAINT IF NOT EXISTS FOR (e:Enum) REQUIRE e.fullName IS UNIQUE").ExecuteAsync();

        // Vector index for semantic search
        await driver.ExecutableQuery(@"
            CREATE VECTOR INDEX code_embeddings IF NOT EXISTS
            FOR (n:Embeddable) ON (n.embedding)
            OPTIONS {indexConfig: {
                `vector.dimensions`: 1024,
                `vector.similarity_function`: 'cosine'
            }}").ExecuteAsync();

        // Fulltext index for keyword search
        await driver.ExecutableQuery(@"
            CREATE FULLTEXT INDEX embeddable_fulltext IF NOT EXISTS
            FOR (n:Embeddable) ON EACH [n.name, n.fullName, n.searchText]").ExecuteAsync();

        Console.WriteLine("Schema initialized (constraints + indexes).");
    }
}