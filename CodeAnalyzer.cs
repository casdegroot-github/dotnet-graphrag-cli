using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

namespace GraphRagCli;

public record NamespaceInfo(string Name, string FilePath);
public record ClassInfo(string FullName, string Name, string Namespace, string FilePath, string Visibility, bool IsStatic, string? BaseClass, List<string> Interfaces, string Kind, string SourceText);
public record InterfaceInfo(string FullName, string Name, string Namespace, string FilePath, string Visibility, List<string> BaseInterfaces, string SourceText);
public record MethodInfo(string FullName, string Name, string ContainingType, string FilePath, string Visibility, string ReturnType, bool IsStatic, bool IsExtensionMethod, string? ExtendedType, List<ParameterInfo> Parameters, string SourceText);
public record ParameterInfo(string Name, string Type, bool IsThis);
public record CallInfo(string CallerFullName, string CalleeFullName);
public record ReferenceInfo(string SourceFullName, string TargetTypeFullName, string Context);
public record EnumInfo(string FullName, string Name, string Namespace, string FilePath, string Visibility, List<string> Members);

public class CodeAnalyzer
{
    public record AnalysisResult(
        List<NamespaceInfo> Namespaces,
        List<ClassInfo> Classes,
        List<InterfaceInfo> Interfaces,
        List<MethodInfo> Methods,
        List<CallInfo> Calls,
        List<ReferenceInfo> References,
        List<EnumInfo> Enums);

    static CodeAnalyzer()
    {
        MSBuildLocator.RegisterDefaults();
    }

    /// <summary>
    /// Loads a solution via MSBuildWorkspace for full symbol resolution,
    /// then extracts symbols per project.
    /// </summary>
    public async Task<Dictionary<string, AnalysisResult>> AnalyzeSolutionAsync(string solutionPath, bool skipTests, bool skipSamples)
    {
        Console.WriteLine($"Loading solution: {solutionPath}");
        using var workspace = MSBuildWorkspace.Create();
        workspace.WorkspaceFailed += (_, e) =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                Console.WriteLine($"  Workspace warning: {e.Diagnostic.Message}");
        };

        var solution = await workspace.OpenSolutionAsync(solutionPath);
        Console.WriteLine($"Loaded {solution.Projects.Count()} projects from solution");

        var results = new Dictionary<string, AnalysisResult>();

        foreach (var project in solution.Projects)
        {
            var projectName = project.Name;

            if (skipTests && (projectName.Contains("Test", StringComparison.OrdinalIgnoreCase) ||
                              projectName.Contains("Tests", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine($"  Skipping test project: {projectName}");
                continue;
            }

            if (skipSamples && (projectName.Contains("Sample", StringComparison.OrdinalIgnoreCase) ||
                                projectName.Contains("Example", StringComparison.OrdinalIgnoreCase) ||
                                projectName.Contains("Playground", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine($"  Skipping sample project: {projectName}");
                continue;
            }

            var compilation = await project.GetCompilationAsync();
            if (compilation == null)
            {
                Console.WriteLine($"  Warning: Could not compile {projectName}");
                continue;
            }

            var namespaces = new List<NamespaceInfo>();
            var classes = new List<ClassInfo>();
            var interfaces = new List<InterfaceInfo>();
            var methods = new List<MethodInfo>();
            var calls = new List<CallInfo>();
            var typeRefs = new List<ReferenceInfo>();
            var enums = new List<EnumInfo>();

            foreach (var tree in compilation.SyntaxTrees)
            {
                var filePath = tree.FilePath;

                // Skip generated/obj files
                if (string.IsNullOrEmpty(filePath) ||
                    filePath.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                    filePath.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                    continue;

                try
                {
                    var semanticModel = compilation.GetSemanticModel(tree);
                    var root = await tree.GetRootAsync();
                    ExtractSymbols(root, semanticModel, filePath, namespaces, classes, interfaces, methods, calls, typeRefs, enums);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Warning: Failed to analyze {Path.GetFileName(filePath)}: {ex.Message}");
                }
            }

            Console.WriteLine($"Extracted from {projectName}: {namespaces.Count} namespaces, {classes.Count} classes, {interfaces.Count} interfaces, {methods.Count} methods, {calls.Count} calls, {typeRefs.Count} references, {enums.Count} enums");

            if (classes.Count > 0 || interfaces.Count > 0)
            {
                results[projectName] = new AnalysisResult(namespaces, classes, interfaces, methods, calls, typeRefs, enums);
            }
            else
            {
                Console.WriteLine($"  (no classes/interfaces found, skipping)");
            }
        }

        return results;
    }

    private static void ExtractSymbols(
        SyntaxNode root,
        SemanticModel semanticModel,
        string filePath,
        List<NamespaceInfo> namespaces,
        List<ClassInfo> classes,
        List<InterfaceInfo> interfaces,
        List<MethodInfo> methods,
        List<CallInfo> calls,
        List<ReferenceInfo> references,
        List<EnumInfo> enums)
    {
        // Extract namespaces
        foreach (var ns in root.DescendantNodes().OfType<NamespaceDeclarationSyntax>())
        {
            var name = ns.Name.ToString();
            if (!namespaces.Any(n => n.Name == name))
                namespaces.Add(new NamespaceInfo(name, filePath));
        }
        foreach (var ns in root.DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>())
        {
            var name = ns.Name.ToString();
            if (!namespaces.Any(n => n.Name == name))
                namespaces.Add(new NamespaceInfo(name, filePath));
        }

        // Extract classes (ClassDeclarationSyntax)
        foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            ExtractClassLike(classDecl, semanticModel, filePath, "class", classes, methods, calls, references);
        }

        // Extract structs (StructDeclarationSyntax)
        foreach (var structDecl in root.DescendantNodes().OfType<StructDeclarationSyntax>())
        {
            ExtractClassLike(structDecl, semanticModel, filePath, "struct", classes, methods, calls, references);
        }

        // Extract records (RecordDeclarationSyntax)
        foreach (var recordDecl in root.DescendantNodes().OfType<RecordDeclarationSyntax>())
        {
            var kind = recordDecl.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword)
                ? "record struct"
                : "record";
            ExtractClassLike(recordDecl, semanticModel, filePath, kind, classes, methods, calls, references);
        }

        // Extract interfaces
        foreach (var ifaceDecl in root.DescendantNodes().OfType<InterfaceDeclarationSyntax>())
        {
            var symbol = semanticModel.GetDeclaredSymbol(ifaceDecl);
            if (symbol == null) continue;

            var fullName = symbol.ToDisplayString();
            var baseIfaces = symbol.Interfaces.Select(i => i.ToDisplayString()).ToList();

            interfaces.Add(new InterfaceInfo(
                fullName, symbol.Name,
                symbol.ContainingNamespace?.ToDisplayString() ?? "",
                filePath,
                symbol.DeclaredAccessibility.ToString().ToLower(),
                baseIfaces,
                ifaceDecl.ToString()));

            // Extract methods from interface
            foreach (var methodDecl in ifaceDecl.Members.OfType<MethodDeclarationSyntax>())
            {
                ExtractMethod(methodDecl, semanticModel, fullName, filePath, methods, calls, references);
            }
        }

        // Extract enums
        foreach (var enumDecl in root.DescendantNodes().OfType<EnumDeclarationSyntax>())
        {
            var symbol = semanticModel.GetDeclaredSymbol(enumDecl);
            if (symbol == null) continue;

            var members = enumDecl.Members.Select(m => m.Identifier.Text).ToList();

            enums.Add(new EnumInfo(
                symbol.ToDisplayString(),
                symbol.Name,
                symbol.ContainingNamespace?.ToDisplayString() ?? "",
                filePath,
                symbol.DeclaredAccessibility.ToString().ToLower(),
                members));
        }
    }

    private static void ExtractClassLike(
        TypeDeclarationSyntax typeDecl,
        SemanticModel semanticModel,
        string filePath,
        string kind,
        List<ClassInfo> classes,
        List<MethodInfo> methods,
        List<CallInfo> calls,
        List<ReferenceInfo> references)
    {
        var symbol = semanticModel.GetDeclaredSymbol(typeDecl);
        if (symbol == null) return;

        var fullName = symbol.ToDisplayString();
        var baseClass = symbol.BaseType?.SpecialType == SpecialType.System_Object
            ? null
            : symbol.BaseType?.ToDisplayString();

        // For structs, base type is System.ValueType — treat as null
        if (kind is "struct" or "record struct" && baseClass == "System.ValueType")
            baseClass = null;

        var ifaces = symbol.Interfaces.Select(i => i.ToDisplayString()).ToList();

        classes.Add(new ClassInfo(
            fullName, symbol.Name,
            symbol.ContainingNamespace?.ToDisplayString() ?? "",
            filePath,
            symbol.DeclaredAccessibility.ToString().ToLower(),
            symbol.IsStatic,
            baseClass, ifaces,
            kind,
            typeDecl.ToString()));

        // Extract methods
        foreach (var methodDecl in typeDecl.Members.OfType<MethodDeclarationSyntax>())
        {
            ExtractMethod(methodDecl, semanticModel, fullName, filePath, methods, calls, references);
        }
    }

    private static void ExtractMethod(
        MethodDeclarationSyntax methodDecl,
        SemanticModel semanticModel,
        string containingType,
        string filePath,
        List<MethodInfo> methods,
        List<CallInfo> calls,
        List<ReferenceInfo> references)
    {
        var symbol = semanticModel.GetDeclaredSymbol(methodDecl);
        if (symbol == null) return;

        var parameters = symbol.Parameters.Select(p => new ParameterInfo(
            p.Name,
            p.Type.ToDisplayString(),
            p.IsThis
        )).ToList();

        // Detect extension methods
        var isExtension = symbol.IsExtensionMethod;
        string? extendedType = null;
        if (isExtension && symbol.Parameters.Length > 0)
        {
            extendedType = symbol.Parameters[0].Type.ToDisplayString();
        }

        var callerFullName = symbol.ToDisplayString();

        methods.Add(new MethodInfo(
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
            methodDecl.ToString()));

        // Extract CALLS from invocations
        foreach (var invocation in methodDecl.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol invokedMethod)
                continue;

            // Use OriginalDefinition to get the unsubstituted generic method,
            // and ReducedFrom to resolve extension method calls back to their static declaration
            var resolved = invokedMethod.ReducedFrom?.OriginalDefinition
                        ?? invokedMethod.OriginalDefinition;

            calls.Add(new CallInfo(callerFullName, resolved.ToDisplayString()));
        }

        // Extract REFERENCES from return type
        if (symbol.ReturnType?.SpecialType == SpecialType.None)
        {
            references.Add(new ReferenceInfo(callerFullName, symbol.ReturnType.ToDisplayString(), "returnType"));
        }

        // Extract REFERENCES from parameters
        foreach (var param in symbol.Parameters)
        {
            if (param.Type.SpecialType == SpecialType.None)
            {
                references.Add(new ReferenceInfo(callerFullName, param.Type.ToDisplayString(), "parameter"));
            }
        }
    }
}
