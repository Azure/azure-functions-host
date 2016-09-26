using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Logging
{
    /// <summary>
    /// Factory object to instantiate new instances of the logging interfaces
    /// </summary>
    public static class LogFactory
    {
        /// <summary>
        /// Get a reader that reads from the given table. 
        /// </summary>
        /// <param name="table"></param>
        /// <returns></returns>
        public static ILogReader NewReader(CloudTable table)
        {
            return new LogReader(table);
        }

        /// <summary>
        /// Create a new writer for the given compute container name that writes to the given table.
        /// Multiple compute instances can write to the same table simultaneously without interference. 
        /// </summary>
        /// <param name="computerContainerName">name of the compute container. Likley %COMPUTERNAME%. </param>
        /// <param name="table">underlying azure storage table to write to.</param>
        /// <returns></returns>
        public static ILogWriter NewWriter(string computerContainerName, CloudTable table)
        {
            return new LogWriter(computerContainerName, table);
        }

        /// <summary>
        /// Default name for fast log tables.
        /// </summary>
        public const string DefaultLogTableName = "AzureWebJobsHostLogs";
    }
}
