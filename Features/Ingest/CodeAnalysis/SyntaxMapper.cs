using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GraphRagCli.Features.Ingest.Analysis;

internal static class SyntaxMapper
{
    public record MethodResult(MethodInfo Method, List<CallInfo> Calls, List<ReferenceInfo> References);

    public static ClassInfo ToClassInfo(TypeDeclarationSyntax node, INamedTypeSymbol symbol, string filePath)
    {
        var kind = node.Kind() switch
        {
            SyntaxKind.ClassDeclaration => "class",
            SyntaxKind.StructDeclaration => "struct",
            SyntaxKind.RecordDeclaration => "record",
            SyntaxKind.RecordStructDeclaration => "record struct",
            _ => throw new ArgumentOutOfRangeException(nameof(node), node.Kind(), "Unexpected type declaration kind")
        };

        // Structs always inherit System.ValueType in the CLR, and classes implicitly inherit System.Object.
        // Neither is a meaningful user-defined base class, so we null them out.
        var isStruct = node.Kind() is SyntaxKind.StructDeclaration or SyntaxKind.RecordStructDeclaration;
        var baseClass = isStruct || symbol.BaseType?.SpecialType == SpecialType.System_Object
            ? null
            : symbol.BaseType?.ToDisplayString();

        return new ClassInfo(
            symbol.ToDisplayString(),
            symbol.Name,
            symbol.ContainingNamespace?.ToDisplayString() ?? "",
            filePath,
            symbol.DeclaredAccessibility.ToString().ToLower(),
            symbol.IsStatic,
            baseClass,
            symbol.Interfaces.Select(i => i.ToDisplayString()).ToList(),
            kind,
            node.ToString());
    }

    public static InterfaceInfo ToInterfaceInfo(InterfaceDeclarationSyntax node, INamedTypeSymbol symbol, string filePath)
    {
        return new InterfaceInfo(
            symbol.ToDisplayString(),
            symbol.Name,
            symbol.ContainingNamespace?.ToDisplayString() ?? "",
            filePath,
            symbol.DeclaredAccessibility.ToString().ToLower(),
            symbol.Interfaces.Select(i => i.ToDisplayString()).ToList(),
            node.ToString());
    }

    public static EnumInfo ToEnumInfo(EnumDeclarationSyntax node, INamedTypeSymbol symbol, string filePath)
    {
        return new EnumInfo(
            symbol.ToDisplayString(),
            symbol.Name,
            symbol.ContainingNamespace?.ToDisplayString() ?? "",
            filePath,
            symbol.DeclaredAccessibility.ToString().ToLower(),
            node.Members.Select(m => m.Identifier.Text).ToList());
    }

    public static MethodResult ToMethodResult(
        MethodDeclarationSyntax node, IMethodSymbol symbol, SemanticModel semanticModel,
        string containingType, string filePath)
    {
        var parameters = symbol.Parameters.Select(p => new ParameterInfo(
            p.Name,
            p.Type.ToDisplayString()
        )).ToList();

        var isExtension = symbol.IsExtensionMethod;
        string? extendedType = null;
        if (isExtension && symbol.Parameters.Length > 0)
            extendedType = symbol.Parameters[0].Type.ToDisplayString();

        var callerFullName = symbol.ToDisplayString();

        var method = new MethodInfo(
            callerFullName,
            symbol.Name,
            containingType,
            filePath,
            symbol.DeclaredAccessibility.ToString().ToLower(),
            symbol.ReturnType.ToDisplayString(),
            symbol.IsStatic,
            isExtension,
            extendedType,
            parameters,
            node.ToString());

        var calls = new List<CallInfo>();
        new InvocationWalker(semanticModel, callerFullName, calls).Visit(node);

        // Only track references to non-primitive types (SpecialType.None).
        // Built-in types like int, string, void don't add value as graph edges.
        var references = new List<ReferenceInfo>();
        if (symbol.ReturnType?.SpecialType == SpecialType.None)
            references.Add(new ReferenceInfo(callerFullName, symbol.ReturnType.ToDisplayString(), "returnType"));

        foreach (var param in symbol.Parameters)
        {
            if (param.Type.SpecialType == SpecialType.None)
                references.Add(new ReferenceInfo(callerFullName, param.Type.ToDisplayString(), "parameter"));
        }

        return new MethodResult(method, calls, references);
    }

    internal class InvocationWalker(SemanticModel semanticModel, string callerFullName, List<CallInfo> calls) : CSharpSyntaxWalker
    {
        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (semanticModel.GetSymbolInfo(node).Symbol is IMethodSymbol invokedMethod)
            {
                // ReducedFrom gives the original definition for extension methods called as instance methods.
                // Fall back to OriginalDefinition for regular methods and unbound generics.
                var resolved = invokedMethod.ReducedFrom?.OriginalDefinition
                            ?? invokedMethod.OriginalDefinition;

                calls.Add(new CallInfo(callerFullName, resolved.ToDisplayString()));
            }

            base.VisitInvocationExpression(node);
        }
    }
}
