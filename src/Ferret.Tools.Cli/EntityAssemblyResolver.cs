using System.Reflection;
using Ferret.Abstractions.Attributes;

namespace Ferret.Tools.Cli;

public static class EntityAssemblyResolver
{
    public static IReadOnlyList<Assembly> Resolve(
        IReadOnlyList<string> assemblyPaths,
        string workingDirectory,
        TextWriter? diagnostics = null)
    {
        if (assemblyPaths.Count > 0)
        {
            return assemblyPaths
                .Select(LoadFromPath)
                .Where(ExportsSearchableEntity)
                .ToList();
        }

        var discovered = DiscoverInDirectory(workingDirectory, diagnostics);
        return discovered;
    }

    private static IReadOnlyList<Assembly> DiscoverInDirectory(string directory, TextWriter? diagnostics)
    {
        var matches = new List<Assembly>();
        foreach (var dll in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            Assembly asm;
            try
            {
                asm = Assembly.LoadFrom(dll);
            }
            catch (Exception ex) when (ex is BadImageFormatException or FileLoadException)
            {
                continue;
            }

            if (ExportsSearchableEntity(asm))
                matches.Add(asm);
        }

        if (matches.Count > 1)
        {
            diagnostics?.WriteLine(
                "Multiple assemblies in the working directory export [SearchableEntity] types: " +
                string.Join(", ", matches.Select(a => a.GetName().Name)) +
                ". Pass --assembly <path> to choose one.");
        }

        return matches;
    }

    private static Assembly LoadFromPath(string path)
    {
        var full = Path.GetFullPath(path);
        return Assembly.LoadFrom(full);
    }

    private static bool ExportsSearchableEntity(Assembly assembly)
    {
        foreach (var type in SafeGetTypes(assembly))
        {
            if ((type.IsPublic || type.IsNestedPublic)
                && type.GetCustomAttribute<SearchableEntityAttribute>() is not null)
            {
                return true;
            }
        }
        return false;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
