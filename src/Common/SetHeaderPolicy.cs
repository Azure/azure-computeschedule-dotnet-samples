
using Azure.Core;
using Azure.Core.Pipeline;

namespace UtilityMethods;
public sealed class SetHeaderPolicy : HttpPipelinePolicy
{
    public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
    {
        // Example: Add or override custom header for triggering completion notification
        message.Request.Headers.SetValue("x-ms-sa-completion-notification",
            message.Request.Headers.TryGetValue("x-ms-sa-completion-notification", out var shouldTrigger)
                ? shouldTrigger
                : "false");

        ProcessNext(message, pipeline);
    }

    public override ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
    {
        Process(message, pipeline);
        return ProcessNextAsync(message, pipeline);
    }
}