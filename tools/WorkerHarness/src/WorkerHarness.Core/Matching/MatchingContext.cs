namespace WorkerHarness.Core
{
    /// <summary>
    /// Encapsulate the criteria to match a Message to a Grpc StreamingMessage
    /// </summary>
    public class MatchingContext : Expression
    {
        // the type of the match. The default is string comparison.
        public string Type { get; set; } = "string";

        // The property of a Grpc StreamingMessage to match against
        public string Query { get; set; } = string.Empty;

        // The expected value of the property being queried
        public string Expected { get; set; } = string.Empty;

        public override void ConstructExpression()
        {
            SetExpression(Expected);
        }
    }
}