using GraphRagCli.Shared;
using Neo4j.Driver;

namespace GraphRagCli.Features.Ingest.GraphDb;

public class Neo4jIngestRepository(IDriver driver)
{
    // --- Node ingestion ---

    public async Task IngestSolutionNodeAsync(string solutionName, IEnumerable<string> projectNames)
    {
        await driver
            .ExecutableQuery("MERGE (s:Solution {fullName: $name})")
            .WithParameters(new { name = solutionName })
            .ExecuteAsync();

        await driver
            .ExecutableQuery(@"
                UNWIND $projects AS projName
                MATCH (s:Solution {fullName: $solution}), (p:Project {fullName: projName})
                MERGE (p)-[:BELONGS_TO_SOLUTION]->(s)")
            .WithParameters(new { solution = solutionName, projects = projectNames.ToList() })
            .ExecuteAsync();
    }

    public async Task IngestProjectNodeAsync(string projectName, DateTime runTimestamp)
    {
        await driver
            .ExecutableQuery("MERGE (p:Project {fullName: $name}) SET p.lastIngestedAt = $runTimestamp")
            .WithParameters(new { name = projectName, runTimestamp })
            .ExecuteAsync();
    }

    public async Task IngestNamespaceNodesAsync(string projectName, List<NamespaceInfo> namespaces, DateTime runTimestamp)
    {
        foreach (var chunk in namespaces.Chunk(100))
        {
            await driver
                .ExecutableQuery(@"
                    UNWIND $batch AS item
                    MERGE (n:Namespace {fullName: item.name})
                    SET n.filePath = item.filePath,
                        n.lastIngestedAt = $runTimestamp
                    WITH n, item
                    MATCH (p:Project {fullName: $project})
                    MERGE (n)-[r:BELONGS_TO_PROJECT]->(p)
                    SET r.lastIngestedAt = $runTimestamp")
                .WithParameters(new
                {
                    batch = chunk.Select(n => new { name = n.Name, filePath = n.FilePath }).ToList(),
                    project = projectName,
                    runTimestamp
                })
                .ExecuteAsync();
        }
    }

    public async Task IngestClassNodesAsync(List<ClassInfo> classes, DateTime runTimestamp)
    {
        foreach (var chunk in classes.Chunk(100))
        {
            await driver
                .ExecutableQuery(@"
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
                        c.needsSummary = CASE WHEN c.bodyHash IS NULL OR c.bodyHash <> item.bodyHash THEN true ELSE c.needsSummary END,
                        c.bodyHash = item.bodyHash,
                        c.lastIngestedAt = $runTimestamp")
                .WithParameters(new
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
                        bodyHash = Hasher.HashCodeBody(c.SourceText ?? c.FullName)
                    }).ToList(),
                    runTimestamp
                })
                .ExecuteAsync();
        }
    }

    public async Task IngestInterfaceNodesAsync(List<InterfaceInfo> interfaces, DateTime runTimestamp)
    {
        foreach (var chunk in interfaces.Chunk(100))
        {
            await driver
                .ExecutableQuery(@"
                    UNWIND $batch AS item
                    MERGE (i:Interface {fullName: item.fullName})
                    SET i.name = item.name,
                        i.namespace = item.namespace,
                        i.filePath = item.filePath,
                        i.visibility = item.visibility,
                        i.sourceText = item.sourceText,
                        i.needsSummary = CASE WHEN i.bodyHash IS NULL OR i.bodyHash <> item.bodyHash THEN true ELSE i.needsSummary END,
                        i.bodyHash = item.bodyHash,
                        i.lastIngestedAt = $runTimestamp")
                .WithParameters(new
                {
                    batch = chunk.Select(i => new
                    {
                        fullName = i.FullName,
                        name = i.Name,
                        @namespace = i.Namespace,
                        filePath = i.FilePath,
                        visibility = i.Visibility,
                        sourceText = i.SourceText,
                        bodyHash = Hasher.HashCodeBody(i.SourceText ?? i.FullName)
                    }).ToList(),
                    runTimestamp
                })
                .ExecuteAsync();
        }
    }

    public async Task IngestEnumNodesAsync(List<EnumInfo> enums, DateTime runTimestamp)
    {
        foreach (var chunk in enums.Chunk(100))
        {
            await driver
                .ExecutableQuery(@"
                    UNWIND $batch AS item
                    MERGE (e:Enum {fullName: item.fullName})
                    SET e.name = item.name,
                        e.namespace = item.namespace,
                        e.filePath = item.filePath,
                        e.visibility = item.visibility,
                        e.members = item.members,
                        e.needsSummary = CASE WHEN e.bodyHash IS NULL OR e.bodyHash <> item.bodyHash THEN true ELSE e.needsSummary END,
                        e.bodyHash = item.bodyHash,
                        e.lastIngestedAt = $runTimestamp")
                .WithParameters(new
                {
                    batch = chunk.Select(e => new
                    {
                        fullName = e.FullName,
                        name = e.Name,
                        @namespace = e.Namespace,
                        filePath = e.FilePath,
                        visibility = e.Visibility,
                        members = string.Join(", ", e.Members),
                        bodyHash = Hasher.Hash(string.Join(", ", e.Members))
                    }).ToList(),
                    runTimestamp
                })
                .ExecuteAsync();
        }
    }

    public async Task IngestMethodNodesAsync(string projectName, List<MethodInfo> methods, List<CallInfo> calls, DateTime runTimestamp)
    {
        var callCountByMethod = calls
            .GroupBy(c => c.CallerFullName)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var chunk in methods.Chunk(100))
        {
            await driver
                .ExecutableQuery(@"
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
                        m.needsSummary = CASE WHEN m.bodyHash IS NULL OR m.bodyHash <> item.bodyHash THEN true ELSE m.needsSummary END,
                        m.bodyHash = item.bodyHash,
                        m.lastIngestedAt = $runTimestamp")
                .WithParameters(new
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
                        bodyHash = Hasher.HashCodeBody(m.SourceText ?? m.FullName)
                    }).ToList(),
                    runTimestamp
                })
                .ExecuteAsync();
        }
    }

    // --- Edge ingestion ---

    public async Task IngestDefinedByEdgesAsync(List<MethodInfo> methods, DateTime runTimestamp)
    {
        foreach (var chunk in methods.Chunk(100))
        {
            await driver
                .ExecutableQuery(@"
                    UNWIND $batch AS item
                    MATCH (m:Method {fullName: item.fullName})
                    OPTIONAL MATCH (c:Class {fullName: item.containingType})
                    OPTIONAL MATCH (i:Interface {fullName: item.containingType})
                    WITH m, coalesce(c, i) AS parent
                    WHERE parent IS NOT NULL
                    MERGE (m)-[r:DEFINED_BY]->(parent)
                    SET r.lastIngestedAt = $runTimestamp")
                .WithParameters(new
                {
                    batch = chunk.Select(m => new
                    {
                        fullName = m.FullName,
                        containingType = m.ContainingType
                    }).ToList(),
                    runTimestamp
                })
                .ExecuteAsync();
        }
    }

    public async Task IngestInheritanceEdgesAsync(List<ClassInfo> classes, DateTime runTimestamp)
    {
        var batch = classes
            .Where(c => c.BaseClass != null)
            .Select(c => new { child = c.FullName, parent = c.BaseClass! })
            .ToList();

        foreach (var chunk in batch.Chunk(100))
        {
            await driver
                .ExecutableQuery(@"
                    UNWIND $batch AS item
                    MATCH (c:Class {fullName: item.child})
                    MATCH (base:Class {fullName: item.parent})
                    MERGE (c)-[r:INHERITS_FROM]->(base)
                    SET r.lastIngestedAt = $runTimestamp")
                .WithParameters(new { batch = chunk.ToList(), runTimestamp })
                .ExecuteAsync();
        }
    }

    public async Task IngestImplementsEdgesAsync(List<ClassInfo> classes, DateTime runTimestamp)
    {
        var batch = classes
            .SelectMany(c => c.Interfaces.Select(i => new { @class = c.FullName, iface = i }))
            .ToList();

        foreach (var chunk in batch.Chunk(100))
        {
            await driver
                .ExecutableQuery(@"
                    UNWIND $batch AS item
                    MATCH (c:Class {fullName: item.class})
                    MATCH (i:Interface {fullName: item.iface})
                    MERGE (c)-[r:IMPLEMENTS]->(i)
                    SET r.lastIngestedAt = $runTimestamp")
                .WithParameters(new { batch = chunk.ToList(), runTimestamp })
                .ExecuteAsync();
        }
    }

    public async Task IngestInterfaceInheritanceEdgesAsync(List<InterfaceInfo> interfaces, DateTime runTimestamp)
    {
        var batch = interfaces
            .SelectMany(i => i.BaseInterfaces.Select(b => new { child = i.FullName, parent = b }))
            .ToList();

        foreach (var chunk in batch.Chunk(100))
        {
            await driver
                .ExecutableQuery(@"
                    UNWIND $batch AS item
                    MATCH (i:Interface {fullName: item.child})
                    MATCH (base:Interface {fullName: item.parent})
                    MERGE (i)-[r:INHERITS_FROM]->(base)
                    SET r.lastIngestedAt = $runTimestamp")
                .WithParameters(new { batch = chunk.ToList(), runTimestamp })
                .ExecuteAsync();
        }
    }

    public async Task IngestNamespaceMembershipEdgesAsync(
        List<ClassInfo> classes, List<InterfaceInfo> interfaces, List<EnumInfo> enums, DateTime runTimestamp)
    {
        foreach (var (label, items) in new[]
        {
            (NodeLabels.Class, classes.Where(c => !string.IsNullOrEmpty(c.Namespace)).Select(c => new { fullName = c.FullName, @namespace = c.Namespace }).ToList()),
            (NodeLabels.Interface, interfaces.Where(i => !string.IsNullOrEmpty(i.Namespace)).Select(i => new { fullName = i.FullName, @namespace = i.Namespace }).ToList()),
            (NodeLabels.Enum, enums.Where(e => !string.IsNullOrEmpty(e.Namespace)).Select(e => new { fullName = e.FullName, @namespace = e.Namespace }).ToList())
        })
        {
            foreach (var chunk in items.Chunk(100))
            {
                await driver
                    .ExecutableQuery($@"
                        UNWIND $batch AS item
                        MATCH (node:{label} {{fullName: item.fullName}})
                        MATCH (n:Namespace {{fullName: item.namespace}})
                        MERGE (node)-[r:BELONGS_TO_NAMESPACE]->(n)
                        SET r.lastIngestedAt = $runTimestamp")
                    .WithParameters(new { batch = chunk.ToList(), runTimestamp })
                    .ExecuteAsync();
            }
        }
    }

    public async Task IngestExtensionMethodEdgesAsync(List<MethodInfo> methods, DateTime runTimestamp)
    {
        var extensionMethods = methods.Where(m => m.IsExtensionMethod && m.ExtendedType != null).ToList();
        if (extensionMethods.Count == 0) return;

        foreach (var chunk in extensionMethods.Chunk(100))
        {
            await driver
                .ExecutableQuery(@"
                    UNWIND $batch AS item
                    MATCH (m:Method {fullName: item.method})
                    OPTIONAL MATCH (target:Class {fullName: item.targetType})
                    OPTIONAL MATCH (targetI:Interface {fullName: item.targetType})
                    WITH m, coalesce(target, targetI) AS t
                    WHERE t IS NOT NULL
                    MERGE (m)-[r:EXTENDS]->(t)
                    SET r.lastIngestedAt = $runTimestamp")
                .WithParameters(new
                {
                    batch = chunk.Select(e => new { method = e.FullName, targetType = e.ExtendedType }).ToList(),
                    runTimestamp
                })
                .ExecuteAsync();
        }
    }

    public async Task IngestCalledByEdgesAsync(List<CallInfo> calls, DateTime runTimestamp)
    {
        var filtered = calls.Where(c => c.CallerFullName != c.CalleeFullName).ToList();

        foreach (var chunk in filtered.Chunk(100))
        {
            await driver
                .ExecutableQuery(@"
                    UNWIND $batch AS item
                    MATCH (caller:Method {fullName: item.caller})
                    MATCH (callee:Method {fullName: item.callee})
                    MERGE (callee)-[r:CALLED_BY]->(caller)
                    SET r.lastIngestedAt = $runTimestamp")
                .WithParameters(new
                {
                    batch = chunk.Select(c => new { caller = c.CallerFullName, callee = c.CalleeFullName }).ToList(),
                    runTimestamp
                })
                .ExecuteAsync();
        }
    }

    public async Task IngestReferenceEdgesAsync(List<ReferenceInfo> references, DateTime runTimestamp)
    {
        foreach (var chunk in references.Chunk(100))
        {
            await driver
                .ExecutableQuery(@"
                    UNWIND $batch AS item
                    MATCH (m:Method {fullName: item.source})
                    OPTIONAL MATCH (target:Class {fullName: item.target})
                    OPTIONAL MATCH (targetI:Interface {fullName: item.target})
                    OPTIONAL MATCH (targetE:Enum {fullName: item.target})
                    WITH m, coalesce(target, targetI, targetE) AS t, item
                    WHERE t IS NOT NULL
                    MERGE (m)-[r:REFERENCES {context: item.context}]->(t)
                    SET r.lastIngestedAt = $runTimestamp")
                .WithParameters(new
                {
                    batch = chunk.Select(r => new { source = r.SourceFullName, target = r.TargetTypeFullName, context = r.Context }).ToList(),
                    runTimestamp
                })
                .ExecuteAsync();
        }
    }
}