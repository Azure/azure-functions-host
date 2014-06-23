namespace Microsoft.Azure.Jobs.Host
{
    // Names of containers used only by hosts (not directly part of the protocol with the dashboard, though other parts
    // may point to blobs stored here).
    internal static class HostContainerNames
    {
        public const string Hosts = "azure-jobs-hosts";
    }
}
