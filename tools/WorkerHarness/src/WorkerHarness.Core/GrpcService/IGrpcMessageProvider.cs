using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using System.Text.Json.Nodes;

namespace WorkerHarness.Core
{
    /// <summary>
    /// Abtract the responsibility to create a Grpc Message
    /// </summary>
    public interface IGrpcMessageProvider
    {
        /// <summary>
        /// Create a StreamingMessage object
        /// </summary>
        /// <param name="contentCase" cref="string">the type of StreamingMessage</param>
        /// <param name="content" cref="string">the content to create the StreamingMessage</param>
        /// <returns></returns>
        StreamingMessage Create(string contentCase, JsonNode? content);
    }
}
