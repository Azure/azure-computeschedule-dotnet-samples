namespace BulkCreateCustom;

internal sealed record CreateCommand(string ConfigPath, bool Verbose)
{
    public static CreateCommand Parse(string[] args)
    {
        string? path = null;
        var verbose = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--config" when path is null && i + 1 < args.Length:
                    path = args[++i];
                    if (string.IsNullOrWhiteSpace(path) || path.StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException("Supply a file path after --config.");
                    break;
                case "--verbose" when !verbose:
                    verbose = true;
                    break;
                default:
                    throw new ArgumentException("Usage: [--config <path>] [--verbose], --validate, or --delete-batch-b <UUID> [--config <path>] [--execute] [--verbose]. Normal execution creates 150 billable VMs.");
            }
        }
        return new(Path.GetFullPath(path ?? DemoConfig.DefaultPath), verbose);
    }
}
