namespace WorkerHarness.Core
{
    /// <summary>
    /// Encapsulate the criteria to match a Message to a Grpc StreamingMessage
    /// </summary>
    public class MatchingCriteria
    {
        // The property of a Grpc StreamingMessage to match against
        public string? Query { get; set; }

        // The expected value of the property being queried
        public string? Expected { get; set; }

        // Wrap the expected value in an Expression object
        public Expression? ExpectedExpression { get; set; }
    }
}