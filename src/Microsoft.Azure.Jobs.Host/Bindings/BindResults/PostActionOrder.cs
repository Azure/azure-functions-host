namespace Microsoft.Azure.Jobs
{
    /// <summary>
    /// Represents the order in which a bind result post action executes.
    /// </summary>
    internal enum PostActionOrder : int
    {
        Default = 0,
        QueueOutput = 1
    }
}
