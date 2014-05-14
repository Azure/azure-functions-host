namespace Microsoft.Azure.Jobs.Protocols
{
    /// <summary>Defines common values for <see cref="TriggerAndOverrideMessage.Reason"/>.</summary>
    public static class TriggerAndOverrideMessageReasons
    {
        /// <summary>A function was run from the dashboard.</summary>
        public static readonly string RunFromDashboard = "Ran from Dashboard.";

        /// <summary>A function was replayed from the dashboard.</summary>
        public static readonly string ReplayFromDashboard = "Replayed from Dashboard.";
    }
}
