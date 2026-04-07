namespace ExecuteVDICreateFlex;

/// <summary>
/// Holds all configuration values needed to run the ExecuteCreateFlex operation,
/// loaded from the .env file at startup.
/// </summary>
internal record FlexCreateConfig(
    string Location,
    string SubscriptionId,
    string ResourceGroupName,
    string VnetName,
    string SubnetName,
    string VmPrefix,
    string VmAdminUsername,
    string VmAdminPassword)
{
    /// <summary>
    /// Loads configuration from the .env file and returns a populated <see cref="FlexCreateConfig"/>.
    /// </summary>
    public static FlexCreateConfig Load()
    {
        var envValues = LoadEnvValues();

        return new FlexCreateConfig(
            Location: GetRequiredValue("AZURE_LOCATION", envValues),
            SubscriptionId: GetRequiredValue("AZURE_SUBSCRIPTION_ID", envValues),
            ResourceGroupName: GetRequiredValue("AZURE_RESOURCE_GROUP", envValues),
            VnetName: GetRequiredValue("AZURE_VNET_NAME", envValues),
            SubnetName: GetRequiredValue("AZURE_SUBNET_NAME", envValues),
            VmPrefix: GetRequiredValue("AZURE_VM_PREFIX", envValues),
            VmAdminUsername: GetRequiredValue("AZURE_VM_ADMIN_USERNAME", envValues),
            VmAdminPassword: GetRequiredValue("AZURE_VM_ADMIN_PASSWORD", envValues));
    }

    private static Dictionary<string, string> LoadEnvValues()
    {
        foreach (var envPath in GetCandidateEnvPaths())
        {
            if (!File.Exists(envPath))
            {
                continue;
            }

            var values = ParseEnvFile(envPath);
            if (values.Count > 0)
            {
                return values;
            }
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetCandidateEnvPaths()
    {
        var candidatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var current = Path.GetFullPath(root);
            for (var i = 0; i < 10; i++)
            {
                candidatePaths.Add(Path.Combine(current, ".env"));
                candidatePaths.Add(Path.Combine(current, "ExecuteVDICreateFlex", ".env"));
                candidatePaths.Add(Path.Combine(current, "src", "ExecuteVDICreateFlex", ".env"));
                candidatePaths.Add(Path.Combine(current, "ExecuteCreateFlex", ".env"));
                candidatePaths.Add(Path.Combine(current, "src", "ExecuteCreateFlex", ".env"));

                var parent = Directory.GetParent(current);
                if (parent == null)
                {
                    break;
                }

                current = parent.FullName;
            }
        }

        return candidatePaths;
    }

    private static Dictionary<string, string> ParseEnvFile(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(key))
            {
                values[key] = value;
            }
        }

        return values;
    }

    private static string GetRequiredValue(string key, IReadOnlyDictionary<string, string> envValues)
    {
        envValues.TryGetValue(key, out var valueFromFile);
        var value = !string.IsNullOrWhiteSpace(valueFromFile)
            ? valueFromFile
            : Environment.GetEnvironmentVariable(key);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required configuration '{key}'. Set it in .env or environment variables.");
        }

        return value;
    }
}
