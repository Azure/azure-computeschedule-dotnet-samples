using System.Text.Json;
using BulkCreateCustom;

if (args is ["--validate"])
    return await LocalValidation.RunAsync();

// A delete invocation must never fall through to create or require VM credentials.
if (args.Contains("--delete-batch-b", StringComparer.Ordinal))
    return await BulkDeleteDemo.RunCommandAsync(args);

try
{
    return await BulkCreateDemo.RunCommandAsync(CreateCommand.Parse(args));
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
catch (JsonException)
{
    Console.Error.WriteLine("Invalid configuration JSON; consult config.example.json. Values omitted for safety.");
    return 2;
}
catch (FileNotFoundException)
{
    Console.Error.WriteLine("Configuration file not found. Create src\\BulkCreateCustom\\config.json from config.example.json, or supply --config <path>. Rebuild if the source folder was moved.");
    return 2;
}
catch (IOException)
{
    Console.Error.WriteLine("Configuration or log file I/O failed. No automatic resubmission; if submission started, Azure operations may still be running.");
    return 2;
}
catch (UnauthorizedAccessException)
{
    Console.Error.WriteLine("Configuration or log file access denied.");
    return 2;
}
