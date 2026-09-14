namespace BulkCreateCustom;

internal sealed class DemoOutput : IDisposable
{
    private readonly StreamWriter file;
    public DemoLog Log { get; }
    public string LogPath { get; }

    private DemoOutput(string path, string? password, bool verbose)
    {
        LogPath = path;
        file = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
        Log = new DemoLog(Console.Out, password, file, verbose);
    }

    public static DemoOutput Create(string prefix, string? password, bool verbose)
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"{prefix}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
        var output = new DemoOutput(path, password, verbose);
        output.Log.Write($"Log file: {path}");
        return output;
    }

    public void Dispose() => file.Dispose();
}

internal sealed class ConsoleCancellation : IDisposable
{
    private readonly CancellationTokenSource source = new();
    public CancellationToken Token => source.Token;

    public ConsoleCancellation() => Console.CancelKeyPress += Cancel;

    private void Cancel(object? sender, ConsoleCancelEventArgs args)
    {
        args.Cancel = true;
        source.Cancel();
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= Cancel;
        source.Dispose();
    }
}
