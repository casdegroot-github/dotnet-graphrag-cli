using System.CommandLine;
using GraphRagCli.Commands;

var root = new RootCommand("GraphRagCli - Build a Code Intelligence Graph in Neo4j");
root.Add(DatabaseCommand.Build());
root.Add(IngestCommand.Build());
root.Add(EmbedCommand.Build());
root.Add(ReembedCommand.Build());
root.Add(SearchCommand.Build());
root.Add(ListCommand.Build());

var config = new CommandLineConfiguration(root);
return await config.InvokeAsync(args);
