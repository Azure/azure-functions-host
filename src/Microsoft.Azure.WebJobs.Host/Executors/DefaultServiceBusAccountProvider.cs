// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
                    // Unlike GetConnectionString below, this property getter does not lock and cache the value.
                    return _ambientConnectionStringProvider.GetConnectionString(ConnectionStringNames.ServiceBus);
                }

                return _connectionString;
            }
            set
            {
                _connectionString = value;
                _connectionStringSet = true;
            }
        }

        public string GetConnectionString()
        {
            if (!_connectionStringSet)
            {
                // Unlike the property getter above, this method locks and caches the value.
                _connectionString = _ambientConnectionStringProvider.GetConnectionString(
                    ConnectionStringNames.ServiceBus);
                _connectionStringSet = true;
            }

            return _connectionString;
        }
    }
}
