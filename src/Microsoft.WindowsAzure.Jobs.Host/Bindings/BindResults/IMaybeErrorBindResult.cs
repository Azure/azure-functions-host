namespace Microsoft.WindowsAzure.Jobs
{
    /// <summary>
    /// Indicates a possible error during runtime binding.
    /// </summary>
    internal interface IMaybeErrorBindResult
    {
        bool IsErrorResult { get; }
    }
}