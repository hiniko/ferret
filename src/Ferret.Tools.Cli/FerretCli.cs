using System.CommandLine;
using System.Reflection;

namespace Ferret.Tools.Cli;

public static class FerretCli
{
    public static RootCommand BuildRootCommand(IReadOnlyList<Assembly> entityAssemblies)
    {
        var context = new ReindexCliContext(entityAssemblies);

        var root = new RootCommand("Ferret command-line tool.");
        root.Subcommands.Add(ReindexCommand.Create(context));
        root.Subcommands.Add(ReindexStatusCommand.Create(context));
        return root;
    }

    public static Task<int> InvokeAsync(
        string[] args,
        IReadOnlyList<Assembly> entityAssemblies,
        TextWriter? output = null,
        TextWriter? error = null,
        CancellationToken ct = default)
    {
        var root = BuildRootCommand(entityAssemblies);
        var config = new InvocationConfiguration
        {
            Output = output ?? Console.Out,
            Error = error ?? Console.Error,
        };
        return root.Parse(args).InvokeAsync(config, ct);
    }
}
