
using Azure.Core;
using Azure.Core.Pipeline;

namespace UtilityMethods;
public sealed class SetHeaderPolicy : HttpPipelinePolicy
{
    private readonly Dictionary<string, string> _headers;

    public SetHeaderPolicy(Dictionary<string, string> headers)
    {
        _headers = headers;
    }

    public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
    {
        foreach (var header in _headers)
        {
            message.Request.Headers.SetValue(header.Key, header.Value);
        }

        ProcessNext(message, pipeline);
    }

    public override ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
    {
        Process(message, pipeline);
        return ProcessNextAsync(message, pipeline);
    }
}