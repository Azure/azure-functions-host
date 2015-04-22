// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal class DefaultServiceBusAccountProvider : IServiceBusAccountProvider
    {
        private static readonly IConnectionStringProvider _ambientConnectionStringProvider =
            AmbientConnectionStringProvider.Instance;

        private string _connectionString;
        private bool _connectionStringSet;

        public string ConnectionString
        {
            get
            {
                if (!_connectionStringSet)
                {
                    _connectionString = _ambientConnectionStringProvider.GetConnectionString(
                        ConnectionStringNames.ServiceBus);
                    _connectionStringSet = true;
                }

                return _connectionString;
            }
            set
            {
                _connectionString = value;
                _connectionStringSet = true;
            }
        }
    }
}
