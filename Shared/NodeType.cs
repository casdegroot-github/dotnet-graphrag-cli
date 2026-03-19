namespace GraphRagCli.Shared;

public static class NodeType
{
    public const string Method = nameof(Method);
    public const string Class = nameof(Class);
    public const string Interface = nameof(Interface);
    public const string Enum = nameof(Enum);
    public const string Namespace = nameof(Namespace);
    public const string Project = nameof(Project);
    public const string Package = nameof(Package);
    public const string Solution = nameof(Solution);

    public static readonly string[] All =
        [Method, Class, Interface, Enum, Namespace, Project, Package, Solution];
}