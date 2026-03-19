namespace GraphRagCli.Shared;

public record EdgeDef(string Source, string RelType, string Target);

public static class GraphSchema
{
    public static readonly EdgeDef[] Edges =
    [
        // Method edges (Source → Target via RelType)
        new(NodeType.Method, RelType.CalledBy,         NodeType.Method),
        new(NodeType.Method, RelType.DefinedBy,        NodeType.Class),
        new(NodeType.Method, RelType.DefinedBy,        NodeType.Interface),
        new(NodeType.Class,     RelType.ReferencedBy,    NodeType.Method),
        new(NodeType.Interface, RelType.ReferencedBy,    NodeType.Method),
        new(NodeType.Enum,      RelType.ReferencedBy,    NodeType.Method),
        new(NodeType.Class,     RelType.RegisteredBy,    NodeType.Method),
        new(NodeType.Interface, RelType.RegisteredBy,    NodeType.Method),
        new(NodeType.Method, RelType.Extends,          NodeType.Class),
        new(NodeType.Method, RelType.ImplementsMethod, NodeType.Method),

        // Type edges
        new(NodeType.Class,     RelType.Implements,         NodeType.Interface),
        new(NodeType.Class,     RelType.BelongsToNamespace, NodeType.Namespace),
        new(NodeType.Interface, RelType.BelongsToNamespace, NodeType.Namespace),
        new(NodeType.Enum,      RelType.BelongsToNamespace, NodeType.Namespace),

        // Container edges
        new(NodeType.Namespace, RelType.BelongsToProject,  NodeType.Project),
        new(NodeType.Project,   RelType.BelongsToSolution, NodeType.Solution),
        new(NodeType.Project,   RelType.BelongsToPackage,  NodeType.Package),
    ];

    /// <summary>All distinct RelTypes that point INTO a given node type.</summary>
    public static List<string> IncomingRelTypes(string targetNodeType) =>
        Edges.Where(e => e.Target == targetNodeType).Select(e => e.RelType).Distinct().ToList();

    /// <summary>All distinct RelTypes across all edges.</summary>
    public static List<string> AllRelTypes() =>
        Edges.Select(e => e.RelType).Distinct().ToList();

    /// <summary>Validates that a prompt builder handles all incoming edges for its node type.</summary>
    public static void ValidateHandledRelTypes(string nodeType, string[] handledRelTypes)
    {
        var expected = IncomingRelTypes(nodeType).ToHashSet();
        var handled = handledRelTypes.ToHashSet();
        var missing = expected.Except(handled).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Build{nodeType}Content does not handle: {string.Join(", ", missing)}");
    }
}
