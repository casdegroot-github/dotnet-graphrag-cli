using System.CommandLine;

namespace GraphRagCli.Shared.Options;

public class SkipOption : Option<string[]>
{
    public SkipOption(string name) : base(name)
    {
        Description = "Skip these pipeline steps (can specify multiple)";
        AllowMultipleArgumentsPerToken = true;
    }
}
