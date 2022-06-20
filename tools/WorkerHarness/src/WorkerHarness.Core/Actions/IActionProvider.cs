using System.Text.Json.Nodes;

namespace WorkerHarness.Core
{
    /// <summary>
    /// Provide an abtraction to create an action
    /// </summary>
    public interface IActionProvider
    {
        public string Type { get; }

        /// <summary>
        /// Create an IAction object from a Json node
        /// </summary>
        /// <param name="actionNode" cref="JsonNode">a JsonNode that encapsulates the information to create an IAction object</param>
        /// <returns>an IAction object</returns>
        IAction Create(JsonNode actionNode);
    }
}
