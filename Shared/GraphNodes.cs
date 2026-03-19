namespace GraphRagCli.Shared;

public record NodeId(string Value);

public record RelatedNode(NodeId Id, string FullName, string? Summary);

public interface IGraphNode
{
    NodeId Id { get; }
    string FullName { get; }
    List<string> Labels { get; }
    string? Summary { get; }
    string? SearchText { get; }
    string? BodyHash { get; }
}

public record MethodNode(
    NodeId Id, string FullName, List<string> Labels,
    string? Summary, string? SearchText, string? BodyHash,
    string? SourceText, string? ReturnType, string? Parameters,
    List<RelatedNode> Calls,
    List<RelatedNode> CalledBy,
    List<RelatedNode> Implements,
    RelatedNode? DefinedIn,
    List<RelatedNode> References,
    List<RelatedNode> Registers,
    List<RelatedNode> Extends
) : IGraphNode;

public record ClassNode(
    NodeId Id, string FullName, List<string> Labels,
    string? Summary, string? SearchText, string? BodyHash,
    string? SourceText,
    List<RelatedNode> Members,
    List<RelatedNode> ReferencedBy,
    List<RelatedNode> RegisteredBy,
    List<RelatedNode> ExtendedBy,
    List<RelatedNode> Implements,
    RelatedNode? Namespace
) : IGraphNode;

public record InterfaceNode(
    NodeId Id, string FullName, List<string> Labels,
    string? Summary, string? SearchText, string? BodyHash,
    string? SourceText,
    List<RelatedNode> Members,
    List<RelatedNode> ImplementedBy,
    List<RelatedNode> ReferencedBy,
    List<RelatedNode> RegisteredBy,
    RelatedNode? Namespace
) : IGraphNode;

public record EnumNode(
    NodeId Id, string FullName, List<string> Labels,
    string? Summary, string? SearchText, string? BodyHash,
    string? SourceText, string? Members,
    List<RelatedNode> ReferencedBy,
    RelatedNode? Namespace
) : IGraphNode;

public record NamespaceNode(
    NodeId Id, string FullName, List<string> Labels,
    string? Summary, string? SearchText, string? BodyHash,
    List<RelatedNode> Types,
    RelatedNode? Project
) : IGraphNode;

public record ProjectNode(
    NodeId Id, string FullName, List<string> Labels,
    string? Summary, string? SearchText, string? BodyHash,
    List<RelatedNode> Namespaces,
    RelatedNode? Solution
) : IGraphNode;

public record PackageNode(
    NodeId Id, string FullName, List<string> Labels,
    string? Summary, string? SearchText, string? BodyHash,
    List<RelatedNode> Projects
) : IGraphNode;

public record SolutionNode(
    NodeId Id, string FullName, List<string> Labels,
    string? Summary, string? SearchText, string? BodyHash,
    List<RelatedNode> Projects
) : IGraphNode;
