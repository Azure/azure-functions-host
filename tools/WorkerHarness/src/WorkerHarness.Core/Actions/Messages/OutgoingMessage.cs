using System.Text.Json.Nodes;

namespace WorkerHarness.Core
{
    /// <summary>
    /// Encapsulate information about an outgoing message mentioned in a scenario file
    /// </summary>
    internal class OutgoingMessage
    {
        // Identifier should be unique within an action's context
        public string Id { get; set; } = Guid.NewGuid().ToString();

        // The type of Grpc StreamingMessage that will be formed and sent to Grpc
        public string ContentCase { get; set; } = string.Empty;

        // The content to form a Grpc StreamingMessage
        public JsonNode Content { get; set; } = new JsonObject();

        // A mapping of variable names to their values/expressions;
        // User can declare and initialize custom variables by populating this property
        public IDictionary<string, string> SetVariables { get; set; } = new Dictionary<string, string>();   
    }
}
