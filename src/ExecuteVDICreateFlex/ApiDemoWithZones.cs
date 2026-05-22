namespace ExecuteVDICreateFlex;

internal static class ApiDemoWithZones
{
    public static async Task RunAsync(int? resourceCountOverride = null, FlexRunLogger? logger = null)
    {
        await ExecuteVDICreateFlexApiDemo.RunAsync(
            "API sample with zones",
            FlexRequestBuilder.BuildRequestWithZones,
            resourceCountOverride,
            logger);
    }
}
