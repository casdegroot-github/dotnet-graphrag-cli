using System.CommandLine;

namespace GraphRagCli.Shared.Options;

public class DatabaseOption : Option<string?>
{
    public DatabaseOption(string name) : base(name)
    {
        Description = "Database container name (auto-detects if only one running)";
        Aliases.Add("-d");
    }
}
