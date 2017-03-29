// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Logging
{
    /// <summary>
    /// Factory object to instantiate new instances of the logging interfaces
    /// </summary>
    public static class LogFactory
    {
        /// <summary>
        /// Get a reader that reads from the given table. A single reader can handle all hosts in the given storage account. 
        /// </summary>
        /// <param name="logTableProvider">callback interface to retrieve logging tables</param>
        /// <returns></returns>
        public static ILogReader NewReader(ILogTableProvider logTableProvider)
        {
            return new LogReader(logTableProvider);
        }

        /// <summary>
        /// Create a new log writer. 
        /// Pass in machineName to facilitate multiple compute instances writing to the same table simultaneously without interference. 
        /// </summary>
        /// <param name="hostName">name of host. A host is a homegenous collection of compute containers, like an Azure Website / appservice. 
        /// Multiple hosts can share a single set of azure tables. Logging is scoped per-host.</param>
        /// <param name="machineName">name of the compute container. Likely %COMPUTERNAME%. </param>
        /// <param name="logTableProvider">callback interface that gets invoked to get azure tables to write logging to.</param>
        /// <param name="onException">An action to be called when the log writer throws an exception.</param>
        /// <returns></returns>
        public static ILogWriter NewWriter(string hostName, string machineName, ILogTableProvider logTableProvider, Action<Exception> onException = null)
        {
            return new LogWriter(hostName, machineName, logTableProvider, onException);
        }

        /// <summary>
        /// Get a default table provider for the given tableClient. This will generate table names with the given prefix.
        /// </summary>
        /// <param name="tableClient">storage client for where to generate tables</param>
        /// <param name="tableNamePrefix">prefix for tables to generate. This should be a valid azure table name.</param>
        public static ILogTableProvider NewLogTableProvider(CloudTableClient tableClient, string tableNamePrefix = LogFactory.DefaultLogTableName)
        {
            return new DefaultLogTableProvider(tableClient, tableNamePrefix);
        }

        /// <summary>
        /// Default name for fast log tables.
        /// </summary>
        public const string DefaultLogTableName = "AzureWebJobsHostLogs";
    }
}
