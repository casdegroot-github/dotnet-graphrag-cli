namespace GraphRagCli.Features.Ingest;

public record AnalysisResult(
    List<NamespaceInfo> Namespaces,
    List<ClassInfo> Classes,
    List<InterfaceInfo> Interfaces,
    List<MethodInfo> Methods,
    List<CallInfo> Calls,
    List<ReferenceInfo> References,
    List<EnumInfo> Enums);

public record NamespaceInfo(string Name, string FilePath);

public record ClassInfo(
    string FullName, string Name, string Namespace, string FilePath,
    string Visibility, bool IsStatic, string? BaseClass, List<string> Interfaces,
    string Kind, string SourceText);

public record InterfaceInfo(
    string FullName, string Name, string Namespace, string FilePath,
    string Visibility, List<string> BaseInterfaces, string SourceText);

public record MethodInfo(
    string FullName, string Name, string ContainingType, string FilePath,
    string Visibility, string ReturnType, bool IsStatic, bool IsExtensionMethod,
    string? ExtendedType, List<ParameterInfo> Parameters, string SourceText);

public record ParameterInfo(string Name, string Type);

public record CallInfo(string CallerFullName, string CalleeFullName);

public record ReferenceInfo(string SourceFullName, string TargetTypeFullName, string Context);

public record EnumInfo(
    string FullName, string Name, string Namespace, string FilePath,
    string Visibility, List<string> Members);

public record EntryPointResult(long LinkedImplementations, long EntryPoints);

public record PublicApiResult(Dictionary<string, long> TypeCounts, long MethodCount, long Total);

public record ReconcileResult(int TransferredEmbeddings, int DeletedEdges, int DeletedNodes);
