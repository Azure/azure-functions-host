namespace WorkerHarness.Core
{
    /// <summary>
    /// Encapsulates information about an incoming message mentioned in a scenario file
    /// </summary>
    internal class IncomingMessage
    {
        // Identifier should be unique within an action's context
        public string? Id { get; set; }

        // The type of Grpc StreamingMessage that a IncomingMessage will be matched against
        public string? ContentCase { get; set; }

        public string? Direction { get; set; }

        // The criteria to match an IncomingMessage to a Grpc StreamingMessage
        public MatchingCriteria? Match { get; set; }

        // The list of validation context that a validator can use to validate a Grpc StreamingMessage
        public ValidationContext[]? Validators { get; set; }

        // A mapping of variable names to their values/expressions
        public IDictionary<string, string>? SetVariables { get; set; }
    }
}
