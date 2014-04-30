namespace Microsoft.Azure.Jobs
{
    /// <summary>Defines a command that may fail gracefully.</summary>
    internal interface ICanFailCommand
    {
        /// <summary>Attempts to execute the command.</summary>
        /// <returns><see langword="false"/> if the command fails gracefully; otherwise <see langword="true"/>.</returns>
        /// <remarks>This method returns <see langword="false"/> rather than throwing to indicate a graceful failure.</remarks>
        bool TryExecute();
    }
}
