namespace GraphRagCli.Shared.Docker;

public record ResolvedConnection(string Uri, string User, string Password);

public record Neo4jContainerInfo(string Name, string Status, string? BoltPort);
