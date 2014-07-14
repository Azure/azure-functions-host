using Microsoft.Azure.Jobs.Host.Executors;

namespace Microsoft.Azure.Jobs
{
    /// <summary>Defines connection string names used by <see cref="IConnectionStringProvider"/>.</summary>
    public static class ConnectionStringNames
    {
        /// <summary>Gets the dashboard connection string name.</summary>
        public static readonly string Dashboard = "Dashboard";

        /// <summary>Gets the Azure Storage connection string name.</summary>
        public static readonly string Storage = "Storage";

        /// <summary>Gets the Azure Service Bus connection string name.</summary>
        public static readonly string ServiceBus = "ServiceBus";
    }
}
