using System.CommandLine;
using CodeGraphIndexer.Commands;

var root = new RootCommand("CodeGraphIndexer - Build a Code Intelligence Graph in Neo4j");
root.Add(IngestCommand.Build());
root.Add(EmbedCommand.Build());
root.Add(ReembedCommand.Build());
root.Add(SearchCommand.Build());

var config = new CommandLineConfiguration(root);
return await config.InvokeAsync(args);
