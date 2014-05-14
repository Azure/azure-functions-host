namespace Microsoft.Azure.Jobs.Protocols
{
    /// <summary>Defines a reader that provides host version information.</summary>
    public interface IHostVersionReader
    {
        /// <summary>Reads all hosts and their versions.</summary>
        /// <returns>All hosts and their versions.</returns>
        HostVersion[] ReadAll();
    }
}
