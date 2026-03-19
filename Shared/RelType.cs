namespace GraphRagCli.Shared;

public static class RelType
{
    // Hierarchy (structural parent-child)
    public const string DefinedBy = "DEFINED_BY";
    public const string BelongsToNamespace = "BELONGS_TO_NAMESPACE";
    public const string BelongsToProject = "BELONGS_TO_PROJECT";
    public const string BelongsToSolution = "BELONGS_TO_SOLUTION";
    public const string BelongsToPackage = "BELONGS_TO_PACKAGE";
    public const string ContainsProject = "CONTAINS_PROJECT";

    // Semantic
    public const string CalledBy = "CALLED_BY";
    public const string ReferencedBy = "REFERENCED_BY";
    public const string Implements = "IMPLEMENTS";
    public const string ImplementsMethod = "IMPLEMENTS_METHOD";
    public const string Extends = "EXTENDS";
    public const string InheritsFrom = "INHERITS_FROM";
    public const string RegisteredBy = "REGISTERED_BY";
}
