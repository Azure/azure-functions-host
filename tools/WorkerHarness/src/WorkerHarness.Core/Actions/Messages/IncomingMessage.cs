namespace WorkerHarness.Core
{
    /// <summary>
    /// Encapsulates information about an incoming message mentioned in a scenario file
    /// </summary>
    public class IncomingMessage
    {
        // Identifier should be unique within an action's context
        public string Id { get; set; } = Guid.NewGuid().ToString();

        // The type of Grpc StreamingMessage that a IncomingMessage will be matched against
        public string ContentCase { get; set; } = string.Empty;

        public string Direction { get; set; } = string.Empty;

        // The criteria to match an IncomingMessage to a Grpc StreamingMessage
        public IList<MatchingContext> Match { get; set; } = new List<MatchingContext>();

        // The list of validation context that a validator can use to validate a Grpc StreamingMessage
        public IList<ValidationContext> Validators { get; set; } = new List<ValidationContext>();

        // A mapping of variable names to their values/expressions
        public IDictionary<string, string> SetVariables { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Check whether all variable dependencies in Match and Validators have been resolved
        /// </summary>
        /// <returns></returns>
        internal bool DependenciesResolved()
        {
            foreach (var match in Match)
            {
                if (!match.Resolved)
                {
                    return false;
                }
            }

            foreach (var validator in Validators)
            {
                if (!validator.Resolved)
                {
                    return false;
                }
            }

            return true;
        }

    }
}
