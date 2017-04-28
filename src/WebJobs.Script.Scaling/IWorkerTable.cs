// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Scaling
{
    /// <summary>
    /// worker table implementation providing CRUD over the table
    /// </summary>
    public interface IWorkerTable
    {
        /// <summary>
        /// enumerate all workers in the table
        /// </summary>
        Task<IEnumerable<IWorkerInfo>> List();

        /// <summary>
        /// add new worker to the table
        /// </summary>
        Task AddOrUpdate(IWorkerInfo worker);

        /// <summary>
        /// delete worker from the table
        /// </summary>
        Task Delete(IWorkerInfo worker);

        /// <summary>
        /// acquire lock on the table
        /// </summary>
        Task<ILockHandle> AcquireLock();

        /// <summary>
        /// get the current manager
        /// </summary>
        Task<IWorkerInfo> GetManager();

        /// <summary>
        /// set the current manager
        /// </summary>
        Task SetManager(IWorkerInfo worker);
    }
}
