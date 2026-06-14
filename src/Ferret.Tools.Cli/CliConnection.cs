namespace Ferret.Tools.Cli;

internal static class CliConnection
{
    public const string EnvVar = "FERRET_CONNECTION";

    public static string? Resolve(string? fromOption)
    {
        if (!string.IsNullOrWhiteSpace(fromOption))
            return fromOption;

        var fromEnv = Environment.GetEnvironmentVariable(EnvVar);
        return string.IsNullOrWhiteSpace(fromEnv) ? null : fromEnv;
    }
}
