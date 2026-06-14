using Ferret.Tools.Cli;

var assemblyPaths = new List<string>();
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] is "--assembly")
        assemblyPaths.Add(args[i + 1]);
}

var entityAssemblies = EntityAssemblyResolver.Resolve(
    assemblyPaths,
    Directory.GetCurrentDirectory(),
    Console.Error);

if (entityAssemblies.Count == 0)
{
    Console.Error.WriteLine(
        "No assembly exporting [SearchableEntity] types was found. " +
        "Pass --assembly <path> or run from a directory containing the built assembly.");
    return 2;
}

return await FerretCli.InvokeAsync(args, entityAssemblies);
