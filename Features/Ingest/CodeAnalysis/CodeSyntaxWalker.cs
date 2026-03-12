using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GraphRagCli.Features.Ingest.Analysis;

internal class CodeSyntaxWalker(
    SemanticModel semanticModel,
    string filePath,
    string assemblyName,
    List<NamespaceInfo> namespaces,
    List<ClassInfo> classes,
    List<InterfaceInfo> interfaces,
    List<MethodInfo> methods,
    List<CallInfo> calls,
    List<ReferenceInfo> references,
    List<EnumInfo> enums) : CSharpSyntaxWalker
{
    private string? _containingType;

    public override void VisitCompilationUnit(CompilationUnitSyntax node)
    {
        var globalStatements = node.Members.OfType<GlobalStatementSyntax>().ToList();
        if (globalStatements.Count > 0)
            VisitTopLevelStatements(node, globalStatements);

        base.VisitCompilationUnit(node);
    }

    public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
    {
        AddNamespace(node.Name.ToString());
        base.VisitNamespaceDeclaration(node);
    }

    public override void VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
    {
        AddNamespace(node.Name.ToString());
        base.VisitFileScopedNamespaceDeclaration(node);
    }

    public override void VisitClassDeclaration(ClassDeclarationSyntax node) => VisitClassLike(node);

    public override void VisitStructDeclaration(StructDeclarationSyntax node) => VisitClassLike(node);

    public override void VisitRecordDeclaration(RecordDeclarationSyntax node) => VisitClassLike(node);

    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node) =>
        VisitInterfaceLike(node);

    public override void VisitEnumDeclaration(EnumDeclarationSyntax node) =>
        VisitEnumLike(node);

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node) =>
        VisitMethodLike(node);

    // Prevent the base walker from recursing into global statements —
    // they are handled explicitly by VisitTopLevelStatements.
    public override void VisitGlobalStatement(GlobalStatementSyntax node) { }

    private void VisitTopLevelStatements(CompilationUnitSyntax root, List<GlobalStatementSyntax> globalStatements)
    {
        var ns = assemblyName;
        var classFullName = $"{ns}.Program";
        var methodFullName = $"{classFullName}.Main()";

        AddNamespace(ns);

        var sourceText = string.Join("\n", globalStatements.Select(g => g.ToString()));

        classes.Add(new ClassInfo(
            classFullName, "Program", ns, filePath,
            "internal", IsStatic: true, BaseClass: null, Interfaces: [], Kind: "class",
            SourceText: sourceText));

        methods.Add(new MethodInfo(
            methodFullName, "Main", classFullName, filePath,
            "private", "void", IsStatic: true, IsExtensionMethod: false,
            ExtendedType: null, Parameters: [], SourceText: sourceText));

        // Walk all global statements for invocations and type references
        var invocationCalls = new List<CallInfo>();
        var invocationWalker = new SyntaxMapper.InvocationWalker(semanticModel, methodFullName, invocationCalls);
        foreach (var stmt in globalStatements)
            invocationWalker.Visit(stmt);
        calls.AddRange(invocationCalls);

        // Extract type references from variable declarations and object creations
        foreach (var stmt in globalStatements)
        {
            foreach (var creation in stmt.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                if (semanticModel.GetSymbolInfo(creation.Type).Symbol is INamedTypeSymbol createdType
                    && createdType.SpecialType == SpecialType.None)
                    references.Add(new ReferenceInfo(methodFullName, createdType.ToDisplayString(), "creation"));
            }

            foreach (var typeNode in stmt.DescendantNodes().OfType<GenericNameSyntax>())
            {
                if (semanticModel.GetSymbolInfo(typeNode).Symbol is INamedTypeSymbol genericType
                    && genericType.SpecialType == SpecialType.None)
                    references.Add(new ReferenceInfo(methodFullName, genericType.ToDisplayString(), "generic"));
            }
        }
    }

    private void AddNamespace(string name)
    {
        if (namespaces.All(n => n.Name != name))
            namespaces.Add(new NamespaceInfo(name, filePath));
    }

    private void VisitClassLike(TypeDeclarationSyntax node)
    {
        var symbol = semanticModel.GetDeclaredSymbol(node);
        if (symbol == null) return;

        classes.Add(SyntaxMapper.ToClassInfo(node, symbol, filePath));

        // Save/restore _containingType to handle nested types correctly.
        // base.Visit* recurses into children, which may include nested classes.
        var previousContainingType = _containingType;
        _containingType = symbol.ToDisplayString();

        switch (node)
        {
            case ClassDeclarationSyntax classNode:
                base.VisitClassDeclaration(classNode);
                break;
            case StructDeclarationSyntax structNode:
                base.VisitStructDeclaration(structNode);
                break;
            case RecordDeclarationSyntax recordNode:
                base.VisitRecordDeclaration(recordNode);
                break;
        }

        _containingType = previousContainingType;
    }

    private void VisitInterfaceLike(InterfaceDeclarationSyntax node)
    {
        var symbol = semanticModel.GetDeclaredSymbol(node);
        if (symbol == null) return;

        interfaces.Add(SyntaxMapper.ToInterfaceInfo(node, symbol, filePath));

        var previousContainingType = _containingType;
        _containingType = symbol.ToDisplayString();
        base.VisitInterfaceDeclaration(node);
        _containingType = previousContainingType;
    }

    private void VisitEnumLike(EnumDeclarationSyntax node)
    {
        var symbol = semanticModel.GetDeclaredSymbol(node);
        if (symbol == null) return;

        enums.Add(SyntaxMapper.ToEnumInfo(node, symbol, filePath));
    }

    private void VisitMethodLike(MethodDeclarationSyntax node)
    {
        if (_containingType == null) return;

        var symbol = semanticModel.GetDeclaredSymbol(node);
        if (symbol == null) return;

        var result = SyntaxMapper.ToMethodResult(node, symbol, semanticModel, _containingType, filePath);
        methods.Add(result.Method);
        calls.AddRange(result.Calls);
        references.AddRange(result.References);
    }
}
