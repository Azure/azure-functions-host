// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Logging
{
    /// <summary>
    /// Callback interface for returning azure tables used for logging. Logs are split across multiple tables based on timestamp. 
    /// </summary>
    public interface ILogTableProvider
    {
        /// <summary>
        /// Return a table with the given suffix. 
        /// The table does not need to exist yet. The logging client that calls this method will create the table if it doesn't exist. 
        /// </summary>
        /// <param name="suffix">The suffix to use for the log table name. This will container only legal table name characters.</param>
        CloudTable GetTable(string suffix);

        /// <summary>
        /// List all tables that we may have handed out. 
        /// Each table is a month's worth of data, so this is expected to be a small set. 
        /// </summary>
        /// <returns></returns>
        Task<CloudTable[]> ListTablesAsync();
    }
}