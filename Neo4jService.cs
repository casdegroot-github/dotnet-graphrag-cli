using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Neo4j.Driver;

namespace GraphRagCli;

public enum QueryMode { Name, Semantic, Graph }

public class Neo4jService : IAsyncDisposable
{
    private readonly IDriver _driver;
    private readonly string? _database;

    public Neo4jService(string uri, string username, string password, string? database = null)
    {
        _driver = GraphDatabase.Driver(uri, AuthTokens.Basic(username, password));
        _database = database;
    }

    private IAsyncSession OpenSession() =>
        _database is not null
            ? _driver.AsyncSession(cfg => cfg.WithDatabase(_database))
            : _driver.AsyncSession();

    public async Task<bool> VerifyConnectivityAsync(bool silent = false)
    {
        try
        {
            await _driver.VerifyConnectivityAsync();
            return true;
        }
        catch (Exception ex)
        {
            if (!silent)
                Console.WriteLine($"Neo4j connection failed: {ex.Message}");
            return false;
        }
    }

    public async Task InitializeSchemaAsync()
    {
        await using var session = OpenSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync("CREATE CONSTRAINT IF NOT EXISTS FOR (s:Solution) REQUIRE s.fullName IS UNIQUE");
            await tx.RunAsync("CREATE CONSTRAINT IF NOT EXISTS FOR (p:Project) REQUIRE p.fullName IS UNIQUE");
            await tx.RunAsync("CREATE CONSTRAINT IF NOT EXISTS FOR (c:Class) REQUIRE c.fullName IS UNIQUE");
            await tx.RunAsync("CREATE CONSTRAINT IF NOT EXISTS FOR (i:Interface) REQUIRE i.fullName IS UNIQUE");
            await tx.RunAsync("CREATE CONSTRAINT IF NOT EXISTS FOR (m:Method) REQUIRE m.fullName IS UNIQUE");
            await tx.RunAsync("CREATE CONSTRAINT IF NOT EXISTS FOR (n:Namespace) REQUIRE n.fullName IS UNIQUE");
            await tx.RunAsync("CREATE CONSTRAINT IF NOT EXISTS FOR (e:Enum) REQUIRE e.fullName IS UNIQUE");
        });
        Console.WriteLine("Schema initialized.");
    }

    public async Task CreateSolutionNodeAsync(string solutionName, IEnumerable<string> projectNames)
    {
        await using var session = OpenSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync(
                "MERGE (s:Solution {fullName: $name})",
                new { name = solutionName });

            await tx.RunAsync(@"
                UNWIND $projects AS projName
                MATCH (s:Solution {fullName: $solution}), (p:Project {fullName: projName})
                MERGE (s)-[:CONTAINS_PROJECT]->(p)",
                new { solution = solutionName, projects = projectNames.ToList() });
        });
        Console.WriteLine($"Solution node '{solutionName}' created with {projectNames.Count()} projects.");
    }

    /// <summary>
    /// Phase 1: Create all nodes (Project, Namespace, Class, Interface, Enum, Method).
    /// Must be called for ALL projects before IngestProjectEdgesAsync.
    /// </summary>
    public async Task IngestProjectNodesAsync(string projectName, CodeAnalyzer.AnalysisResult result)
    {
        await using var session = OpenSession();

        // Create Project node and link to Solution if present
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync(
                "MERGE (p:Project {fullName: $name})",
                new { name = projectName });
        });

        // Ingest Namespaces
        foreach (var chunk in result.Namespaces.Chunk(100))
        {
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(@"
                    UNWIND $batch AS item
                    MERGE (n:Namespace {fullName: item.name})
                    SET n.filePath = item.filePath
                    WITH n, item
                    MATCH (p:Project {fullName: $project})
                    MERGE (p)-[:CONTAINS_NAMESPACE]->(n)",
                    new { batch = chunk.Select(n => new { name = n.Name, filePath = n.FilePath }).ToList(), project = projectName });
            });
        }

        // Ingest Classes (includes structs and records via kind property)
        foreach (var chunk in result.Classes.Chunk(100))
        {
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(@"
                    UNWIND $batch AS item
                    MERGE (c:Class {fullName: item.fullName})
                    SET c.name = item.name,
                        c.namespace = item.namespace,
                        c.filePath = item.filePath,
                        c.visibility = item.visibility,
                        c.isStatic = item.isStatic,
                        c.kind = item.kind,
                        c.baseType = item.baseType,
                        c.sourceText = item.sourceText,
                        c.contentHash = item.contentHash
                    WITH c, item
                    MATCH (p:Project {fullName: $project})
                    MERGE (p)-[:CONTAINS]->(c)",
                    new
                    {
                        batch = chunk.Select(c => new
                        {
                            fullName = c.FullName,
                            name = c.Name,
                            @namespace = c.Namespace,
                            filePath = c.FilePath,
                            visibility = c.Visibility,
                            isStatic = c.IsStatic,
                            kind = c.Kind,
                            baseType = c.BaseClass,
                            sourceText = c.SourceText,
                            contentHash = ComputeHash(c.SourceText ?? c.FullName)
                        }).ToList(),
                        project = projectName
                    });
            });
        }

        // Ingest Interfaces
        foreach (var chunk in result.Interfaces.Chunk(100))
        {
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(@"
                    UNWIND $batch AS item
                    MERGE (i:Interface {fullName: item.fullName})
                    SET i.name = item.name,
                        i.namespace = item.namespace,
                        i.filePath = item.filePath,
                        i.visibility = item.visibility,
                        i.sourceText = item.sourceText,
                        i.contentHash = item.contentHash
                    WITH i, item
                    MATCH (p:Project {fullName: $project})
                    MERGE (p)-[:CONTAINS]->(i)",
                    new
                    {
                        batch = chunk.Select(i => new
                        {
                            fullName = i.FullName,
                            name = i.Name,
                            @namespace = i.Namespace,
                            filePath = i.FilePath,
                            visibility = i.Visibility,
                            sourceText = i.SourceText,
                            contentHash = ComputeHash(i.SourceText ?? i.FullName)
                        }).ToList(),
                        project = projectName
                    });
            });
        }

        // Ingest Enums
        foreach (var chunk in result.Enums.Chunk(100))
        {
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(@"
                    UNWIND $batch AS item
                    MERGE (e:Enum {fullName: item.fullName})
                    SET e.name = item.name,
                        e.namespace = item.namespace,
                        e.filePath = item.filePath,
                        e.visibility = item.visibility,
                        e.members = item.members,
                        e.contentHash = item.contentHash
                    WITH e, item
                    MATCH (p:Project {fullName: $project})
                    MERGE (p)-[:CONTAINS]->(e)",
                    new
                    {
                        batch = chunk.Select(e => new
                        {
                            fullName = e.FullName,
                            name = e.Name,
                            @namespace = e.Namespace,
                            filePath = e.FilePath,
                            visibility = e.Visibility,
                            members = string.Join(", ", e.Members),
                            contentHash = ComputeHash(string.Join(", ", e.Members))
                        }).ToList(),
                        project = projectName
                    });
            });
        }

        // Build per-method extracted call count (includes external calls)
        var callCountByMethod = result.Calls
            .GroupBy(c => c.CallerFullName)
            .ToDictionary(g => g.Key, g => g.Count());

        // Ingest Methods
        foreach (var chunk in result.Methods.Chunk(100))
        {
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(@"
                    UNWIND $batch AS item
                    MERGE (m:Method {fullName: item.fullName})
                    SET m.name = item.name,
                        m.containingType = item.containingType,
                        m.filePath = item.filePath,
                        m.visibility = item.visibility,
                        m.returnType = item.returnType,
                        m.isStatic = item.isStatic,
                        m.isExtensionMethod = item.isExtensionMethod,
                        m.extendedType = item.extendedType,
                        m.parameters = item.parameters,
                        m.sourceText = item.sourceText,
                        m.extractedCallCount = item.extractedCallCount,
                        m.contentHash = item.contentHash",
                    new
                    {
                        batch = chunk.Select(m => new
                        {
                            fullName = m.FullName,
                            name = m.Name,
                            containingType = m.ContainingType,
                            filePath = m.FilePath,
                            visibility = m.Visibility,
                            returnType = m.ReturnType,
                            isStatic = m.IsStatic,
                            isExtensionMethod = m.IsExtensionMethod,
                            extendedType = m.ExtendedType,
                            parameters = string.Join(", ", m.Parameters.Select(p => $"{p.Type} {p.Name}")),
                            sourceText = m.SourceText,
                            extractedCallCount = callCountByMethod.GetValueOrDefault(m.FullName, 0),
                            contentHash = ComputeHash(m.SourceText ?? m.FullName)
                        }).ToList()
                    });
            });
        }

        Console.WriteLine($"  Nodes: {result.Namespaces.Count} namespaces, {result.Classes.Count} classes, {result.Interfaces.Count} interfaces, {result.Methods.Count} methods, {result.Enums.Count} enums");
    }

    /// <summary>
    /// Phase 2: Create all edges. Must be called AFTER all projects' nodes have been created,
    /// so cross-project CALLS/REFERENCES/INHERITS/IMPLEMENTS edges can resolve.
    /// </summary>
    public async Task IngestProjectEdgesAsync(string projectName, CodeAnalyzer.AnalysisResult result)
    {
        await using var session = OpenSession();

        // DEFINES relationships (Class/Interface -> Method)
        foreach (var chunk in result.Methods.Chunk(100))
        {
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(@"
                    UNWIND $batch AS item
                    MATCH (m:Method {fullName: item.fullName})
                    OPTIONAL MATCH (c:Class {fullName: item.containingType})
                    OPTIONAL MATCH (i:Interface {fullName: item.containingType})
                    WITH m, coalesce(c, i) AS parent
                    WHERE parent IS NOT NULL
                    MERGE (parent)-[:DEFINES]->(m)",
                    new
                    {
                        batch = chunk.Select(m => new
                        {
                            fullName = m.FullName,
                            containingType = m.ContainingType
                        }).ToList()
                    });
            });
        }

        // INHERITS_FROM (class inheritance) — MATCH both sides (no phantom nodes)
        var inheritanceBatch = result.Classes
            .Where(c => c.BaseClass != null)
            .Select(c => new { child = c.FullName, parent = c.BaseClass! })
            .ToList();
        foreach (var chunk in inheritanceBatch.Chunk(100))
        {
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(@"
                    UNWIND $batch AS item
                    MATCH (c:Class {fullName: item.child})
                    MATCH (base:Class {fullName: item.parent})
                    MERGE (c)-[:INHERITS_FROM]->(base)",
                    new { batch = chunk.ToList() });
            });
        }

        // IMPLEMENTS (class -> interface) — MATCH both sides (no phantom nodes)
        var implementsBatch = result.Classes
            .SelectMany(c => c.Interfaces.Select(i => new { @class = c.FullName, iface = i }))
            .ToList();
        foreach (var chunk in implementsBatch.Chunk(100))
        {
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(@"
                    UNWIND $batch AS item
                    MATCH (c:Class {fullName: item.class})
                    MATCH (i:Interface {fullName: item.iface})
                    MERGE (c)-[:IMPLEMENTS]->(i)",
                    new { batch = chunk.ToList() });
            });
        }

        // INHERITS_FROM (interface inheritance) — MATCH both sides (no phantom nodes)
        var ifaceInheritanceBatch = result.Interfaces
            .SelectMany(i => i.BaseInterfaces.Select(b => new { child = i.FullName, parent = b }))
            .ToList();
        foreach (var chunk in ifaceInheritanceBatch.Chunk(100))
        {
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(@"
                    UNWIND $batch AS item
                    MATCH (i:Interface {fullName: item.child})
                    MATCH (base:Interface {fullName: item.parent})
                    MERGE (i)-[:INHERITS_FROM]->(base)",
                    new { batch = chunk.ToList() });
            });
        }

        // BELONGS_TO_NAMESPACE for classes
        var classesByNs = result.Classes
            .Where(c => !string.IsNullOrEmpty(c.Namespace))
            .Select(c => new { fullName = c.FullName, @namespace = c.Namespace })
            .ToList();
        foreach (var chunk in classesByNs.Chunk(100))
        {
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(@"
                    UNWIND $batch AS item
                    MATCH (c:Class {fullName: item.fullName})
                    MATCH (n:Namespace {fullName: item.namespace})
                    MERGE (c)-[:BELONGS_TO_NAMESPACE]->(n)",
                    new { batch = chunk.ToList() });
            });
        }

        // BELONGS_TO_NAMESPACE for interfaces
        var ifacesByNs = result.Interfaces
            .Where(i => !string.IsNullOrEmpty(i.Namespace))
            .Select(i => new { fullName = i.FullName, @namespace = i.Namespace })
            .ToList();
        foreach (var chunk in ifacesByNs.Chunk(100))
        {
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(@"
                    UNWIND $batch AS item
                    MATCH (i:Interface {fullName: item.fullName})
                    MATCH (n:Namespace {fullName: item.namespace})
                    MERGE (i)-[:BELONGS_TO_NAMESPACE]->(n)",
                    new { batch = chunk.ToList() });
            });
        }

        // BELONGS_TO_NAMESPACE for enums
        var enumsByNs = result.Enums
            .Where(e => !string.IsNullOrEmpty(e.Namespace))
            .Select(e => new { fullName = e.FullName, @namespace = e.Namespace })
            .ToList();
        foreach (var chunk in enumsByNs.Chunk(100))
        {
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(@"
                    UNWIND $batch AS item
                    MATCH (e:Enum {fullName: item.fullName})
                    MATCH (n:Namespace {fullName: item.namespace})
                    MERGE (e)-[:BELONGS_TO_NAMESPACE]->(n)",
                    new { batch = chunk.ToList() });
            });
        }

        // EXTENDS relationships for extension methods
        var extensionMethods = result.Methods.Where(m => m.IsExtensionMethod && m.ExtendedType != null).ToList();
        if (extensionMethods.Count > 0)
        {
            foreach (var chunk in extensionMethods.Chunk(100))
            {
                await session.ExecuteWriteAsync(async tx =>
                {
                    await tx.RunAsync(@"
                        UNWIND $batch AS item
                        MATCH (m:Method {fullName: item.method})
                        OPTIONAL MATCH (target:Class {fullName: item.targetType})
                        OPTIONAL MATCH (targetI:Interface {fullName: item.targetType})
                        WITH m, coalesce(target, targetI) AS t
                        WHERE t IS NOT NULL
                        MERGE (m)-[:EXTENDS]->(t)",
                        new
                        {
                            batch = chunk.Select(e => new { method = e.FullName, targetType = e.ExtendedType }).ToList()
                        });
                });
            }
        }

        // CALLS relationships
        foreach (var chunk in result.Calls.Chunk(100))
        {
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(@"
                    UNWIND $batch AS item
                    MATCH (caller:Method {fullName: item.caller})
                    MATCH (callee:Method {fullName: item.callee})
                    MERGE (caller)-[:CALLS]->(callee)",
                    new
                    {
                        batch = chunk.Select(c => new { caller = c.CallerFullName, callee = c.CalleeFullName }).ToList()
                    });
            });
        }

        // REFERENCES relationships
        foreach (var chunk in result.References.Chunk(100))
        {
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(@"
                    UNWIND $batch AS item
                    MATCH (m:Method {fullName: item.source})
                    OPTIONAL MATCH (target:Class {fullName: item.target})
                    OPTIONAL MATCH (targetI:Interface {fullName: item.target})
                    OPTIONAL MATCH (targetE:Enum {fullName: item.target})
                    WITH m, coalesce(target, targetI, targetE) AS t, item
                    WHERE t IS NOT NULL
                    MERGE (m)-[:REFERENCES {context: item.context}]->(t)",
                    new
                    {
                        batch = chunk.Select(r => new { source = r.SourceFullName, target = r.TargetTypeFullName, context = r.Context }).ToList()
                    });
            });
        }

        Console.WriteLine($"  Edges: {result.Calls.Count} calls, {result.References.Count} references");
    }

    public async Task LabelEntryPointsAsync()
    {
        await using var session = OpenSession();

        // Label extension methods on DI/hosting types
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync(@"
                MATCH (m:Method)
                WHERE m.isExtensionMethod = true
                  AND (m.extendedType CONTAINS 'IServiceCollection'
                    OR m.extendedType CONTAINS 'IHostBuilder'
                    OR m.extendedType CONTAINS 'IApplicationBuilder'
                    OR m.extendedType CONTAINS 'IEndpointRouteBuilder')
                SET m:EntryPoint");
        });

        // Link interface method declarations to their concrete implementations
        await session.ExecuteWriteAsync(async tx =>
        {
            var result = await tx.RunAsync(@"
                MATCH (iMethod:Method)<-[:DEFINES]-(iface:Interface)<-[:IMPLEMENTS]-(cls:Class)-[:DEFINES]->(cMethod:Method)
                WHERE iMethod.name = cMethod.name
                  AND size(split(iMethod.parameters, ',')) = size(split(cMethod.parameters, ','))
                MERGE (cMethod)-[:IMPLEMENTS_METHOD]->(iMethod)
                RETURN count(*) AS count");
            var record = await result.SingleAsync();
            Console.WriteLine($"Linked {record["count"]} interface method implementations.");
        });

        // Count and report
        await session.ExecuteReadAsync(async tx =>
        {
            var result = await tx.RunAsync("MATCH (e:EntryPoint) RETURN count(e) AS count");
            var record = await result.SingleAsync();
            Console.WriteLine($"Labeled {record["count"]} entry points.");
        });
    }

    public async Task LabelPublicApiAsync(List<string>? nugetProjects)
    {
        await using var session = OpenSession();

        // Label public types as PublicApi, optionally filtered to NuGet projects
        foreach (var (label, display) in new[] { ("Interface", "interfaces"), ("Class", "classes"), ("Enum", "enums") })
        {
            await session.ExecuteWriteAsync(async tx =>
            {
                string query;
                object parameters;

                if (nugetProjects != null)
                {
                    query = "MATCH (p:Project)-[:CONTAINS]->(node:" + label + ") " +
                            "WHERE node.visibility = 'public' AND p.fullName IN $projects " +
                            "SET node:PublicApi RETURN count(DISTINCT node) AS count";
                    parameters = new { projects = nugetProjects };
                }
                else
                {
                    query = "MATCH (node:" + label + ") " +
                            "WHERE node.visibility = 'public' " +
                            "SET node:PublicApi RETURN count(node) AS count";
                    parameters = new { };
                }

                var result = await tx.RunAsync(query, parameters);
                var record = await result.SingleAsync();
                Console.WriteLine($"  {record["count"]} public {display}");
            });
        }

        // Label public methods on public types as PublicApi
        await session.ExecuteWriteAsync(async tx =>
        {
            var result = await tx.RunAsync(@"
                MATCH (parent:PublicApi)-[:DEFINES]->(m:Method)
                WHERE m.visibility = 'public'
                SET m:PublicApi
                RETURN count(m) AS count");
            var record = await result.SingleAsync();
            Console.WriteLine($"  {record["count"]} public methods");
        });

        // Count and report
        await session.ExecuteReadAsync(async tx =>
        {
            var result = await tx.RunAsync(@"
                MATCH (n:PublicApi)
                WITH labels(n) AS lbls, count(n) AS cnt
                UNWIND lbls AS label
                WITH label, sum(cnt) AS total
                WHERE label <> 'PublicApi'
                RETURN 'Total PublicApi: ' + toString(sum(total)) AS summary");
            var record = await result.SingleAsync();
            Console.WriteLine($"  {record["summary"]}");
        });
    }

    // --- Hashing ---

    public static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(bytes);
    }

    // --- Vector Embedding Support ---

    public async Task InitializeVectorSchemaAsync()
    {
        await using var session = OpenSession();

        // Add Embeddable label to all code nodes
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync(
                "MATCH (n) WHERE n:Class OR n:Interface OR n:Method OR n:Enum SET n:Embeddable");
        });

        // Create vector index (ollama embeddings)
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync(@"
                CREATE VECTOR INDEX code_embeddings IF NOT EXISTS
                FOR (n:Embeddable) ON (n.embedding)
                OPTIONS {indexConfig: {
                    `vector.dimensions`: 1024,
                    `vector.similarity_function`: 'cosine'
                }}");
        });

        Console.WriteLine("Vector schema initialized.");
    }

    // --- Fulltext Index (Phase 4) ---

    public async Task InitializeFulltextIndexAsync()
    {
        await using var session = OpenSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync(@"
                CREATE FULLTEXT INDEX embeddable_fulltext IF NOT EXISTS
                FOR (n:Embeddable) ON EACH [n.name, n.fullName, n.searchText]");
        });
        Console.WriteLine("Fulltext index initialized.");
    }

    public record EmbeddableNode(string ElementId, string FullName, string Prompt, IReadOnlyList<string> Labels, string ContentHash);

    public async Task SetEmbeddingsBatchAsync(List<(string ElementId, string Summary, string? SearchText, string[] Tags, float[] Embedding, string ContentHash)> batch)
    {
        await using var session = OpenSession();

        foreach (var chunk in batch.Chunk(50))
        {
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(@"
                    UNWIND $batch AS item
                    MATCH (n) WHERE elementId(n) = item.elementId
                    SET n:Embeddable,
                        n.embedding = item.embedding,
                        n.summary = item.summary,
                        n.searchText = item.searchText,
                        n.tags = item.tags,
                        n.embeddingHash = item.embeddingHash,
                        n.stale = false",
                    new
                    {
                        batch = chunk.Select(x => new
                        {
                            elementId = x.ElementId,
                            summary = x.Summary,
                            searchText = x.SearchText ?? x.Summary,
                            tags = x.Tags.ToList(),
                            embedding = x.Embedding.Select(f => (double)f).ToList(),
                            embeddingHash = x.ContentHash
                        }).ToList()
                    });
            });
        }
    }

    // --- Reembed Support ---

    public record ReembeddableNode(string ElementId, string FullName, string Summary, string? SearchText, string[] Tags, string ContentHash);

    public async Task<List<ReembeddableNode>> GetNodesWithSummariesAsync()
    {
        await using var session = OpenSession();

        var result = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(@"
                MATCH (n:Embeddable)
                WHERE n.summary IS NOT NULL AND n.summary <> ''
                RETURN elementId(n) AS elementId, n.fullName AS fullName, n.summary AS summary,
                       n.searchText AS searchText,
                       n.tags AS tags, n.contentHash AS contentHash");
            return await cursor.ToListAsync();
        });

        return result.Select(r => new ReembeddableNode(
            r["elementId"].As<string>(),
            r["fullName"].As<string>(),
            r["summary"].As<string>(),
            r["searchText"]?.As<string>(),
            r["tags"]?.As<List<string>>()?.ToArray() ?? [],
            r["contentHash"]?.As<string>() ?? ""
        )).ToList();
    }

    // --- Phase 1: Stale Tagging ---

    /// <summary>
    /// Mark dependents of changed nodes as stale so they get re-summarized in the next pass.
    /// </summary>
    public async Task<int> MarkStaleDependentsAsync(List<string> changedFullNames)
    {
        if (changedFullNames.Count == 0) return 0;

        await using var session = OpenSession();
        var markedStale = await session.ExecuteWriteAsync(async tx =>
        {
            var result = await tx.RunAsync(@"
                UNWIND $names AS name
                MATCH (changed {fullName: name})
                OPTIONAL MATCH (dependent)-[:CALLS|DEFINES|IMPLEMENTS|REFERENCES|EXTENDS|INHERITS_FROM]-(changed)
                WHERE dependent:Embeddable
                SET dependent.stale = true
                RETURN count(DISTINCT dependent) AS markedStale",
                new { names = changedFullNames });
            var record = await result.SingleAsync();
            return record["markedStale"].As<int>();
        });
        return markedStale;
    }

    // --- Two-Pass Summarization ---

    public record SummarizableNode(string ElementId, string FullName, string Prompt, IReadOnlyList<string> Labels, string ContentHash, string NodeType);

    /// <summary>
    /// Pass 1: Leaf nodes — Methods with no outgoing CALLS, and all Enums.
    /// </summary>
    public async Task<List<SummarizableNode>> GetLeafNodesForSummarizationAsync(bool onlyChanged, int maxSourceLength = 8000)
    {
        await using var session = OpenSession();

        var hashFilter = onlyChanged
            ? "AND (n.embeddingHash IS NULL OR n.contentHash <> n.embeddingHash)"
            : "";

        var result = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync($@"
                MATCH (n:Embeddable)
                WHERE (
                    (n:Method AND NOT exists {{ (n)-[:CALLS]->() }})
                ) {hashFilter}
                RETURN elementId(n) AS elementId, n.fullName AS fullName,
                       labels(n) AS labels,
                       n.sourceText AS sourceText,
                       n.name AS name,
                       n.returnType AS returnType,
                       n.parameters AS parameters,
                       n.members AS members,
                       n.contentHash AS contentHash");
            return await cursor.ToListAsync();
        });

        return result.Select(r => BuildSummarizableNode(r, null, maxSourceLength: maxSourceLength)).ToList();
    }

    /// <summary>
    /// Pass 2: Non-leaf nodes — Methods that CALL others, Classes, Interfaces.
    /// Uses stale flag + content hash instead of composite hash to avoid hash drift.
    /// </summary>
    public async Task<Dictionary<int, List<string>>> GetPass2TiersAsync(bool onlyChanged)
    {
        await using var session = OpenSession();

        string ChangeFilter(string alias) => onlyChanged
            ? $"AND ({alias}.stale = true OR {alias}.summary IS NULL OR {alias}.contentHash <> {alias}.embeddingHash)"
            : "";

        var result = await session.ExecuteReadAsync(async tx =>
        {
            // Tier = longest CALLS chain to another Pass 2 node
            var cursor = await tx.RunAsync($@"
                MATCH (m:Method:Embeddable)
                WHERE exists {{ (m)-[:CALLS]->() }}
                {ChangeFilter("m")}
                OPTIONAL MATCH path = (m)-[:CALLS*1..20]->(other:Method:Embeddable)
                WHERE exists {{ (other)-[:CALLS]->() }}
                WITH m, CASE WHEN path IS NULL THEN 0 ELSE length(path) END AS depth
                WITH m.fullName AS fullName, max(depth) AS tier
                RETURN fullName, tier ORDER BY tier");
            return await cursor.ToListAsync();
        });

        var tiers = new Dictionary<int, List<string>>();
        var maxMethodTier = 0;

        foreach (var r in result)
        {
            var fullName = r["fullName"].As<string>();
            var tier = r["tier"].As<int>();
            if (tier > maxMethodTier) maxMethodTier = tier;

            if (!tiers.TryGetValue(tier, out var list))
            {
                list = [];
                tiers[tier] = list;
            }
            list.Add(fullName);
        }

        // Classes/interfaces/enums go in the final tier (maxMethodTier + 1)
        var classTier = tiers.Count > 0 ? maxMethodTier + 1 : 0;
        var classResult = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync($@"
                MATCH (n:Embeddable)
                WHERE (n:Class OR n:Interface OR n:Enum)
                {ChangeFilter("n")}
                RETURN n.fullName AS fullName");
            return await cursor.ToListAsync();
        });

        if (classResult.Count > 0)
        {
            if (!tiers.TryGetValue(classTier, out var list))
            {
                list = [];
                tiers[classTier] = list;
            }
            foreach (var r in classResult)
                list.Add(r["fullName"].As<string>());
        }

        return tiers;
    }

    public async Task<List<SummarizableNode>> GetContextualNodesForSummarizationAsync(bool onlyChanged, int maxContextChars = 4000, int maxSourceLength = 8000, IReadOnlyList<string>? filterNames = null)
    {
        await using var session = OpenSession();

        var filter = onlyChanged
            ? "AND (n.stale = true OR n.summary IS NULL OR n.contentHash <> n.embeddingHash)"
            : "";
        var nameFilter = filterNames != null ? "AND n.fullName IN $filterNames" : "";

        var result = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync($@"
                MATCH (n:Embeddable)
                WHERE ((n:Method AND exists {{ (n)-[:CALLS]->() }})
                    OR n:Class
                    OR n:Interface
                    OR n:Enum)
                {filter}
                {nameFilter}
                CALL {{ WITH n OPTIONAL MATCH (n)-[:CALLS]->(callee:Method) RETURN collect(DISTINCT {{name: callee.name, summary: callee.summary, rel: 'Calls'}}) AS callees }}
                CALL {{ WITH n OPTIONAL MATCH (n)-[:DEFINES]->(defined:Method) RETURN collect(DISTINCT {{name: defined.name, summary: defined.summary, rel: 'Defines'}}) AS defined }}
                CALL {{ WITH n OPTIONAL MATCH (n)-[:IMPLEMENTS]->(iface:Interface) RETURN collect(DISTINCT {{name: iface.name, summary: iface.summary, rel: 'Implements'}}) AS ifaces }}
                CALL {{ WITH n OPTIONAL MATCH (impl:Class)-[:IMPLEMENTS]->(n) RETURN collect(DISTINCT {{name: impl.name, summary: impl.summary, rel: 'Implemented by'}}) AS impls }}
                CALL {{ WITH n OPTIONAL MATCH (ref:Method)-[:REFERENCES]->(n) RETURN collect(DISTINCT {{name: ref.name, summary: ref.summary, rel: 'Referenced by'}}) AS refs }}
                CALL {{ WITH n
                    OPTIONAL MATCH (consumer:Method)-[:REFERENCES]->(n)
                    WHERE n:Enum AND consumer.sourceText IS NOT NULL
                    RETURN collect(DISTINCT {{name: consumer.fullName, sourceText: consumer.sourceText, rel: 'ConsumerCode'}}) AS enumConsumers
                }}
                CALL {{ WITH n
                    OPTIONAL MATCH (caller:Method)-[:CALLS|REFERENCES]->(n)
                    WHERE (n:Interface OR n:Class) AND caller.sourceText IS NOT NULL
                      AND NOT EXISTS {{ (caller)<-[:DEFINES]-()-[:IMPLEMENTS]->(n) }}
                      AND NOT EXISTS {{ (caller)<-[:DEFINES]-(n) }}
                    RETURN collect(DISTINCT {{
                        name: caller.fullName, sourceText: caller.sourceText,
                        isEntryPoint: caller:EntryPoint, rel: 'ConsumerSource'
                    }}) AS consumerSources
                }}
                RETURN elementId(n) AS elementId, n.fullName AS fullName,
                       labels(n) AS labels,
                       n.sourceText AS sourceText,
                       n.name AS name,
                       n.returnType AS returnType,
                       n.parameters AS parameters,
                       n.members AS members,
                       n.contentHash AS contentHash,
                       ifaces + callees + defined + impls + refs AS neighborContext,
                       enumConsumers,
                       consumerSources",
                filterNames != null ? new { filterNames } : (object?)null);
            return await cursor.ToListAsync();
        });

        var nodes = new List<SummarizableNode>();
        foreach (var r in result)
        {
            var rawNeighbors = r["neighborContext"].As<List<IDictionary<string, object>?>>();
            var neighbors = rawNeighbors
                .Where(n => n != null)
                .Select(n => (
                    Rel: n!["rel"]?.ToString() ?? "",
                    Name: n["name"]?.ToString() ?? "",
                    Summary: n["summary"]?.ToString() ?? ""))
                .Where(n => n.Name.Length > 0)
                .ToList();

            // Allocate context budget: 60% for consumer source, 40% for neighbor summaries
            var consumerBudget = (int)(maxContextChars * 0.6);
            var neighborBudget = (int)(maxContextChars * 0.4);

            // Extract enum consumer source code
            var rawEnumConsumers = r["enumConsumers"].As<List<IDictionary<string, object>?>>();
            var enumConsumerContext = BuildConsumerCodeContext(rawEnumConsumers, consumerBudget, r["name"]?.As<string>(), r["members"]?.As<string>());

            // Extract interface/class consumer source code
            var rawConsumerSources = r["consumerSources"].As<List<IDictionary<string, object>?>>();
            var consumerSourceContext = BuildConsumerSourceContext(rawConsumerSources, consumerBudget);

            // If we have consumer context, use the split budget; otherwise give full budget to neighbors
            var hasConsumerContext = enumConsumerContext != null || consumerSourceContext != null;
            var effectiveNeighborBudget = hasConsumerContext ? neighborBudget : maxContextChars;

            var context = BuildPrioritizedContext(neighbors, maxChars: effectiveNeighborBudget);

            // Append consumer context sections
            if (enumConsumerContext != null)
                context = (context ?? "") + enumConsumerContext;
            if (consumerSourceContext != null)
                context = (context ?? "") + consumerSourceContext;

            var contentHash = r["contentHash"]?.As<string>() ?? "";
            nodes.Add(BuildSummarizableNode(r, context, contentHash, maxSourceLength));
        }

        return nodes;
    }

    /// <summary>
    /// Builds a context string from neighbors, prioritized by relationship type.
    /// </summary>
    private static string? BuildPrioritizedContext(
        List<(string Rel, string Name, string Summary)> neighbors, int maxChars)
    {
        if (neighbors.Count == 0) return null;

        var priorityOrder = new[] { "Implements", "Calls", "Defines", "Implemented by", "Referenced by" };
        var sorted = neighbors
            .OrderBy(n => Array.IndexOf(priorityOrder, n.Rel) is var idx && idx < 0 ? 99 : idx)
            .ToList();

        var lines = new List<string>();
        var totalLen = 0;

        foreach (var (rel, name, summary) in sorted)
        {
            var truncSummary = summary.Length > 120 ? summary[..117] + "..." : summary;
            var line = string.IsNullOrEmpty(truncSummary)
                ? $"{rel}: {name}"
                : $"{rel}: {name} ({truncSummary})";

            if (totalLen + line.Length > maxChars && lines.Count > 0)
                break;

            lines.Add(line);
            totalLen += line.Length + 3;
        }

        return $"\n\nContext from the codebase graph:\n- {string.Join("\n- ", lines)}";
    }

    /// <summary>
    /// Builds consumer code context for enums — filters source to lines mentioning the enum or its members.
    /// </summary>
    private static string? BuildConsumerCodeContext(List<IDictionary<string, object>?>? consumers, int maxChars, string? enumName, string? members)
    {
        if (consumers == null || consumers.Count == 0 || enumName == null) return null;

        var memberNames = (members ?? "").Split(',', StringSplitOptions.TrimEntries)
            .Where(m => m.Length > 0).ToList();
        var searchTerms = new List<string> { enumName };
        searchTerms.AddRange(memberNames);

        var snippets = new List<string>();
        var totalLen = 0;

        foreach (var c in consumers.Where(c => c != null))
        {
            var name = c!["name"]?.ToString() ?? "";
            var sourceText = c["sourceText"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(sourceText)) continue;

            // Filter to lines mentioning the enum or its members
            var relevantLines = sourceText.Split('\n')
                .Where(line => searchTerms.Any(term => line.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (relevantLines.Count == 0) continue;

            var snippet = $"ConsumerCode: {name}:\n{string.Join("\n", relevantLines)}";
            if (totalLen + snippet.Length > maxChars && snippets.Count > 0) break;
            snippets.Add(snippet);
            totalLen += snippet.Length;
        }

        if (snippets.Count == 0) return null;
        return $"\n\nEnum usage in consumers:\n{string.Join("\n\n", snippets)}";
    }

    /// <summary>
    /// Builds consumer source context for interfaces/classes — prioritizes EntryPoint consumers (DI methods).
    /// </summary>
    private static string? BuildConsumerSourceContext(List<IDictionary<string, object>?>? consumers, int maxChars)
    {
        if (consumers == null || consumers.Count == 0) return null;

        var validConsumers = consumers
            .Where(c => c != null && !string.IsNullOrEmpty(c["sourceText"]?.ToString()))
            .Select(c => (
                Name: c!["name"]?.ToString() ?? "",
                SourceText: c["sourceText"]?.ToString() ?? "",
                IsEntryPoint: c["isEntryPoint"] is true or "true"))
            .OrderByDescending(c => c.IsEntryPoint) // EntryPoint consumers first
            .ToList();

        if (validConsumers.Count == 0) return null;

        var snippets = new List<string>();
        var totalLen = 0;
        var maxPerSnippet = maxChars / Math.Max(validConsumers.Count, 1);

        foreach (var (name, sourceText, isEntryPoint) in validConsumers)
        {
            var truncated = sourceText.Length > maxPerSnippet ? sourceText[..maxPerSnippet] : sourceText;
            var label = isEntryPoint ? "EntryPoint" : "Consumer";
            var snippet = $"{label}: {name}:\n{truncated}";
            if (totalLen + snippet.Length > maxChars && snippets.Count > 0) break;
            snippets.Add(snippet);
            totalLen += snippet.Length;
        }

        if (snippets.Count == 0) return null;
        return $"\n\nKey consumer implementations:\n{string.Join("\n\n", snippets)}";
    }

    private static SummarizableNode BuildSummarizableNode(IRecord record, string? contextSuffix, string? overrideHash = null, int maxSourceLength = 8000)
    {
        var elementId = record["elementId"].As<string>();
        var fullName = record["fullName"].As<string>();
        var labels = record["labels"].As<List<string>>();
        var sourceText = record["sourceText"]?.As<string>();
        var name = record["name"]?.As<string>() ?? fullName;
        var contentHash = overrideHash ?? record["contentHash"]?.As<string>() ?? "";

        if (maxSourceLength > 0 && sourceText != null && sourceText.Length > maxSourceLength)
            sourceText = sourceText[..maxSourceLength];

        var nodeType = labels.Contains("Method") ? "Method"
            : labels.Contains("Interface") ? "Interface"
            : labels.Contains("Enum") ? "Enum"
            : "Class";

        string codeBlock;
        string? members = null;
        if (nodeType == "Method")
        {
            var returnType = record["returnType"]?.As<string>() ?? "void";
            var parameters = record["parameters"]?.As<string>() ?? "";
            var body = sourceText ?? "";
            codeBlock = $"{returnType} {fullName}({parameters})\n{body}";
        }
        else if (nodeType == "Interface")
        {
            codeBlock = sourceText ?? name;
        }
        else if (nodeType == "Enum")
        {
            members = record["members"]?.As<string>() ?? "";
            codeBlock = $"enum {fullName} {{ {members} }}";
        }
        else
        {
            codeBlock = sourceText ?? name;
        }

        // Phase 3: Check for small nodes that don't need LLM
        var smallSummary = OllamaService.TrySmallNodeSummary(nodeType, name, fullName, sourceText, members);
        if (smallSummary != null)
        {
            // Use a special prompt marker so EmbedNodes can detect and skip LLM
            var templatePrompt = $"__TEMPLATE__{smallSummary.Summary}||{string.Join(",", smallSummary.Tags)}";
            return new SummarizableNode(elementId, fullName, templatePrompt, labels, contentHash, nodeType);
        }

        var isEntryPoint = labels.Contains("EntryPoint");
        var prompt = OllamaService.BuildPrompt(codeBlock, nodeType, contextSuffix, isEntryPoint);
        return new SummarizableNode(elementId, fullName, prompt, labels, contentHash, nodeType);
    }

    // --- Search ---

    public record NeighborInfo(string Name, string? Summary, string Relationship);
    public record SearchResult(string FullName, string Name, string? Summary, string? Namespace, string? FilePath, double Score, string Type, List<NeighborInfo>? Neighbors = null, double? PageRank = null, IReadOnlyList<string>? Labels = null, string? Parameters = null, string? ReturnType = null);

    public async Task<List<SearchResult>> SemanticSearchAsync(float[] queryVector, int topK, string? labelFilter)
    {
        await using var session = OpenSession();

        var results = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(@"
                CALL db.index.vector.queryNodes('code_embeddings', $topK, $queryVector)
                YIELD node, score
                WHERE $labelFilter IS NULL OR $labelFilter IN labels(node)
                RETURN node.fullName AS fullName, node.name AS name, node.summary AS summary,
                       node.namespace AS namespace, node.filePath AS filePath, score,
                       [l IN labels(node) WHERE l IN ['Class','Interface','Method','Enum','Namespace','Project']][0] AS type,
                       node.pageRank AS pageRank,
                       labels(node) AS labels,
                       node.parameters AS parameters, node.returnType AS returnType
                ORDER BY score DESC LIMIT $topK",
                new
                {
                    queryVector = queryVector.Select(f => (double)f).ToList(),
                    topK,
                    labelFilter
                });

            return await cursor.ToListAsync();
        });

        return results.Select(r => new SearchResult(
            r["fullName"].As<string>(),
            r["name"].As<string>(),
            r["summary"]?.As<string>(),
            r["namespace"]?.As<string>(),
            r["filePath"]?.As<string>(),
            r["score"].As<double>(),
            r["type"]?.As<string>() ?? "Unknown",
            PageRank: r["pageRank"]?.As<double?>(),
            Labels: r["labels"]?.As<List<string>>(),
            Parameters: r["parameters"]?.As<string>(),
            ReturnType: r["returnType"]?.As<string>()
        )).ToList();
    }

    // --- Phase 4: Hybrid Search ---

    public async Task<List<SearchResult>> FulltextSearchAsync(string query, int topK)
    {
        await using var session = OpenSession();

        var results = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(@"
                CALL db.index.fulltext.queryNodes('embeddable_fulltext', $query)
                YIELD node, score
                RETURN node.fullName AS fullName, node.name AS name, node.summary AS summary,
                       node.namespace AS namespace, node.filePath AS filePath, score,
                       [l IN labels(node) WHERE l IN ['Class','Interface','Method','Enum','Namespace','Project']][0] AS type,
                       node.pageRank AS pageRank,
                       labels(node) AS labels,
                       node.parameters AS parameters, node.returnType AS returnType
                ORDER BY score DESC LIMIT $topK",
                new { query, topK });

            return await cursor.ToListAsync();
        });

        return results.Select(r => new SearchResult(
            r["fullName"].As<string>(),
            r["name"].As<string>(),
            r["summary"]?.As<string>(),
            r["namespace"]?.As<string>(),
            r["filePath"]?.As<string>(),
            r["score"].As<double>(),
            r["type"]?.As<string>() ?? "Unknown",
            PageRank: r["pageRank"]?.As<double?>(),
            Labels: r["labels"]?.As<List<string>>(),
            Parameters: r["parameters"]?.As<string>(),
            ReturnType: r["returnType"]?.As<string>()
        )).ToList();
    }

    public async Task<List<SearchResult>> HybridSearchAsync(string query, float[] queryVector, int topK, double fulltextWeight = 0.5, double vectorWeight = 0.5)
    {
        const int k = 20; // RRF constant — lower k gives wider score spread

        var fulltextTask = FulltextSearchAsync(query, topK * 2);
        var vectorTask = SemanticSearchAsync(queryVector, topK * 2, null);
        await Task.WhenAll(fulltextTask, vectorTask);

        var fulltextResults = fulltextTask.Result;
        var vectorResults = vectorTask.Result;

        // Build rank maps
        var fulltextRanks = new Dictionary<string, int>();
        for (int i = 0; i < fulltextResults.Count; i++)
            fulltextRanks[fulltextResults[i].FullName] = i + 1;

        var vectorRanks = new Dictionary<string, int>();
        for (int i = 0; i < vectorResults.Count; i++)
            vectorRanks[vectorResults[i].FullName] = i + 1;

        // Merge all candidates
        var allCandidates = fulltextResults.Concat(vectorResults)
            .GroupBy(r => r.FullName)
            .Select(g =>
            {
                var best = g.First();
                var ftRank = fulltextRanks.GetValueOrDefault(g.Key, topK * 2 + 1);
                var vecRank = vectorRanks.GetValueOrDefault(g.Key, topK * 2 + 1);
                var rrfScore = fulltextWeight / (k + ftRank) + vectorWeight / (k + vecRank);
                var nameMatch = string.Equals(best.Name, query, StringComparison.OrdinalIgnoreCase) ? 0.3 : 0.0;
                var fullNameMatch = best.FullName.EndsWith(query, StringComparison.OrdinalIgnoreCase) ? 0.2 : 0.0;
                return best with { Score = rrfScore + Math.Max(nameMatch, fullNameMatch) };
            })
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();

        return allCandidates;
    }

    // --- Phase 5: Query Routing ---

    private static readonly Regex PascalCaseRegex = new(@"[A-Z][a-z]+[A-Z]", RegexOptions.Compiled);
    private static readonly string[] GraphKeywords = [
        "call", "calls", "implement", "implements", "depends", "uses", "references", "dependencies", "implementations", "extends", "inherits",
        "how does", "how is", "what does", "explain", "describe",
        "pattern", "architecture", "workflow", "pipeline", "flow",
        "registered", "configured", "composed", "wired"
    ];

    public static QueryMode RouteQuery(string query)
    {
        var lower = query.ToLowerInvariant();

        // Graph mode: relationship queries
        if (GraphKeywords.Any(kw => lower.Contains(kw)))
            return QueryMode.Graph;

        // Graph mode: natural language questions (multi-word with question mark)
        if (lower.Contains('?') && query.Split(' ').Length > 3)
            return QueryMode.Graph;

        // Name mode: PascalCase, dots, or no spaces
        if (PascalCaseRegex.IsMatch(query) || query.Contains('.') || query.Contains("::"))
            return QueryMode.Name;

        // Name mode: single word that looks like an identifier
        if (!query.Contains(' ') && query.Length > 2)
            return QueryMode.Name;

        // Default: semantic
        return QueryMode.Semantic;
    }

    public (double FulltextWeight, double VectorWeight) GetWeightsForMode(QueryMode mode) => mode switch
    {
        QueryMode.Name => (0.7, 0.3),
        QueryMode.Semantic => (0.3, 0.7),
        _ => (0.5, 0.5)
    };

    // --- Graph-Augmented Retrieval ---

    public async Task<List<SearchResult>> GraphAugmentedSearchAsync(float[] queryVector, int topK, string? labelFilter)
    {
        await using var session = OpenSession();

        var candidateCount = topK * 2;

        // Step 1: Vector search for candidates
        var candidates = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(@"
                CALL db.index.vector.queryNodes('code_embeddings', $candidateCount, $queryVector)
                YIELD node, score
                WHERE $labelFilter IS NULL OR $labelFilter IN labels(node)
                RETURN node.fullName AS fullName, node.name AS name, node.summary AS summary,
                       node.namespace AS namespace, node.filePath AS filePath, score,
                       [l IN labels(node) WHERE l IN ['Class','Interface','Method','Enum','Namespace','Project']][0] AS type,
                       node.pageRank AS pageRank,
                       labels(node) AS labels,
                       node.parameters AS parameters, node.returnType AS returnType",
                new
                {
                    candidateCount,
                    queryVector = queryVector.Select(f => (double)f).ToList(),
                    labelFilter
                });
            return await cursor.ToListAsync();
        });

        var candidateResults = candidates.Select(r => new SearchResult(
            r["fullName"].As<string>(),
            r["name"].As<string>(),
            r["summary"]?.As<string>(),
            r["namespace"]?.As<string>(),
            r["filePath"]?.As<string>(),
            r["score"].As<double>(),
            r["type"]?.As<string>() ?? "Unknown",
            PageRank: r["pageRank"]?.As<double?>(),
            Labels: r["labels"]?.As<List<string>>(),
            Parameters: r["parameters"]?.As<string>(),
            ReturnType: r["returnType"]?.As<string>()
        )).ToList();

        return await GraphExpandAndRerankAsync(candidateResults, topK);
    }

    /// <summary>
    /// Post-processing: 1-hop graph expansion + cluster/centrality/entrypoint re-ranking.
    /// Works on candidates from any retrieval method (vector, hybrid, fulltext).
    /// </summary>
    public async Task<List<SearchResult>> GraphExpandAndRerankAsync(List<SearchResult> candidates, int topK)
    {
        if (candidates.Count == 0)
            return [];

        await using var session = OpenSession();

        // 1-hop expansion — collect neighbors for each candidate
        var candidateFullNames = candidates.Select(c => c.FullName).ToList();
        var neighborMap = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(@"
                UNWIND $fullNames AS fn
                MATCH (n:Embeddable {fullName: fn})
                OPTIONAL MATCH (n)-[r]-(neighbor:Embeddable)
                WHERE type(r) IN ['CALLS','IMPLEMENTS','DEFINES','REFERENCES','EXTENDS','INHERITS_FROM']
                RETURN fn AS sourceFullName,
                       collect(DISTINCT {name: neighbor.name, summary: neighbor.summary, relationship: type(r), neighborFullName: neighbor.fullName}) AS neighbors",
                new { fullNames = candidateFullNames });
            return await cursor.ToListAsync();
        });

        var neighborsById = new Dictionary<string, List<NeighborInfo>>();
        var neighborFullNamesById = new Dictionary<string, List<string>>();
        foreach (var record in neighborMap)
        {
            var sourceFullName = record["sourceFullName"].As<string>();
            var neighbors = record["neighbors"].As<List<IDictionary<string, object>>>();
            var neighborInfos = new List<NeighborInfo>();
            var neighborFns = new List<string>();
            foreach (var n in neighbors)
            {
                var nName = n["name"]?.ToString();
                if (nName == null) continue;
                neighborInfos.Add(new NeighborInfo(nName, n["summary"]?.ToString(), n["relationship"]?.ToString() ?? "RELATED"));
                neighborFns.Add(n["neighborFullName"]?.ToString() ?? "");
            }
            neighborsById[sourceFullName] = neighborInfos;
            neighborFullNamesById[sourceFullName] = neighborFns;
        }

        // Cluster re-rank — +0.02 per shared neighbor with other candidates
        var reranked = candidates.Select(c =>
        {
            var bonus = 0.0;
            if (neighborFullNamesById.TryGetValue(c.FullName, out var myNeighborFns))
            {
                foreach (var other in candidateFullNames)
                {
                    if (other == c.FullName) continue;
                    if (neighborFullNamesById.TryGetValue(other, out var otherNeighborFns))
                    {
                        var shared = myNeighborFns.Intersect(otherNeighborFns).Count();
                        bonus += shared * 0.02;
                    }
                }
            }
            var centralityBonus = Math.Min((c.PageRank ?? 0) * 0.1, 0.05);
            var epBonus = c.Labels?.Contains("EntryPoint") == true ? 0.10 : 0.0;
            var neighbors = neighborsById.GetValueOrDefault(c.FullName);
            return c with { Score = c.Score + bonus + centralityBonus + epBonus, Neighbors = neighbors };
        })
        .OrderByDescending(c => c.Score)
        .Take(topK)
        .ToList();

        return reranked;
    }

    // --- Phase 6: Namespace Summaries ---

    public async Task<List<(string ElementId, string Namespace, List<string> MemberSummaries)>> GetNamespaceSummariesAsync()
    {
        await using var session = OpenSession();

        var result = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(@"
                MATCH (n:Embeddable)-[:BELONGS_TO_NAMESPACE]->(ns:Namespace)
                WHERE n.summary IS NOT NULL
                RETURN elementId(ns) AS elementId, ns.fullName AS namespace, collect(n.name + ': ' + n.summary) AS memberSummaries
                ORDER BY ns.fullName");
            return await cursor.ToListAsync();
        });

        return result.Select(r => (
            ElementId: r["elementId"].As<string>(),
            Namespace: r["namespace"].As<string>(),
            MemberSummaries: r["memberSummaries"].As<List<string>>()
        )).ToList();
    }

    public async Task<List<(string ElementId, string Project, List<string> NamespaceSummaries)>> GetProjectSummariesAsync()
    {
        await using var session = OpenSession();

        var result = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(@"
                MATCH (p:Project)-[:CONTAINS_NAMESPACE]->(ns:Namespace)
                WHERE ns.summary IS NOT NULL
                WITH elementId(p) AS elementId, p.fullName AS project, collect(ns.fullName + ': ' + ns.summary) AS namespaceSummaries
                WHERE size(namespaceSummaries) > 0
                RETURN elementId, project, namespaceSummaries
                ORDER BY project");
            return await cursor.ToListAsync();
        });

        return result.Select(r => (
            ElementId: r["elementId"].As<string>(),
            Project: r["project"].As<string>(),
            NamespaceSummaries: r["namespaceSummaries"].As<List<string>>()
        )).ToList();
    }

    public async Task<List<(string ElementId, string SolutionName, List<string> ProjectSummaries)>> GetSolutionSummariesAsync()
    {
        await using var session = OpenSession();

        var result = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(@"
                MATCH (s:Solution)-[:CONTAINS_PROJECT]->(p:Project)
                WHERE p.summary IS NOT NULL
                WITH elementId(s) AS elementId, s.fullName AS solution, collect(p.fullName + ': ' + p.summary) AS projectSummaries
                WHERE size(projectSummaries) > 0
                RETURN elementId, solution, projectSummaries
                ORDER BY solution");
            return await cursor.ToListAsync();
        });

        return result.Select(r => (
            ElementId: r["elementId"].As<string>(),
            SolutionName: r["solution"].As<string>(),
            ProjectSummaries: r["projectSummaries"].As<List<string>>()
        )).ToList();
    }

    // --- Database Info ---

    public async Task<DatabaseInfo> GetDatabaseInfoAsync()
    {
        await using var session = OpenSession();

        return await session.ExecuteReadAsync(async tx =>
        {
            // Node counts by label
            var countCursor = await tx.RunAsync(@"
                MATCH (n)
                WITH [l IN labels(n) WHERE l IN ['Project','Namespace','Class','Interface','Method','Enum']][0] AS label
                WHERE label IS NOT NULL
                RETURN label, count(*) AS count
                ORDER BY count DESC");
            var counts = (await countCursor.ToListAsync())
                .ToDictionary(r => r["label"].As<string>(), r => r["count"].As<long>());

            // Project names and summaries
            var projCursor = await tx.RunAsync(@"
                MATCH (p:Project)
                OPTIONAL MATCH (p)-[:CONTAINS]->(member)
                WITH p, count(member) AS memberCount
                RETURN p.fullName AS name,
                       coalesce(p.searchText, p.summary) AS searchText,
                       memberCount
                ORDER BY p.fullName");
            var projects = (await projCursor.ToListAsync())
                .Select(r => new ProjectInfo(
                    r["name"].As<string>(),
                    r["searchText"].As<string?>(null),
                    r["memberCount"].As<long>()))
                .ToList();

            // Solution summary
            var solCursor = await tx.RunAsync(@"
                MATCH (s:Solution)
                RETURN s.fullName AS name,
                       coalesce(s.searchText, s.summary) AS searchText
                ORDER BY s.fullName");
            var solutions = (await solCursor.ToListAsync())
                .Select(r => new SolutionInfo(
                    r["name"].As<string>(),
                    r["searchText"].As<string?>(null)))
                .ToList();

            // Embedding coverage
            var embedCursor = await tx.RunAsync(@"
                MATCH (n)
                WHERE any(l IN labels(n) WHERE l IN ['Class','Interface','Method','Enum'])
                RETURN count(n) AS total,
                       count(n.embedding) AS embedded");
            var embedRow = await embedCursor.SingleAsync();

            return new DatabaseInfo(
                solutions,
                counts,
                projects,
                embedRow["total"].As<long>(),
                embedRow["embedded"].As<long>());
        });
    }

    public record SolutionInfo(string Name, string? Summary);
    public record ProjectInfo(string Name, string? Summary, long MemberCount);
    public record DatabaseInfo(
        List<SolutionInfo> Solutions,
        Dictionary<string, long> NodeCounts,
        List<ProjectInfo> Projects,
        long TotalEmbeddable,
        long Embedded);

    // --- GDS Centrality ---

    public async Task ComputeCentralityAsync()
    {
        await using var session = OpenSession();

        // Drop existing projection if present
        try
        {
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync("CALL gds.graph.drop('code-graph', false)");
            });
        }
        catch { /* projection may not exist */ }

        // Project the graph
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync(@"
                CALL gds.graph.project(
                    'code-graph',
                    'Embeddable',
                    ['CALLS', 'IMPLEMENTS', 'DEFINES', 'REFERENCES', 'EXTENDS', 'INHERITS_FROM']
                )");
        });
        Console.WriteLine("  GDS graph projected.");

        // Run PageRank
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync(@"
                CALL gds.pageRank.write('code-graph', {writeProperty: 'pageRank'})");
        });
        Console.WriteLine("  PageRank computed.");

        // Run degree centrality (in-degree)
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync(@"
                CALL gds.degree.write('code-graph', {writeProperty: 'inDegree', orientation: 'REVERSE'})");
        });
        Console.WriteLine("  Degree centrality computed.");

        // Drop projection
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync("CALL gds.graph.drop('code-graph')");
        });
        Console.WriteLine("  GDS projection dropped.");
    }

    public async ValueTask DisposeAsync()
    {
        await _driver.DisposeAsync();
    }
}
