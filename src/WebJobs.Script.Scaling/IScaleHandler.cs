// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Scaling
{
    /// <summary>
    /// this is implemented by core function runtime and it will be called by ScaleManager
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix", Justification = "By design")]
    public interface IScaleHandler
    {
        /// <summary>
        /// ScaleManager requests more worker
        /// </summary>
        /// <returns>The name of the worker if added.</returns>
        Task<string> TryAddWorker(string activityId, IEnumerable<string> stampNames, int workers);

        /// <summary>
        /// ScaleManager requests worker removal
        /// </summary>
        /// <returns>True if the worker was removed, false otherwise.</returns>
        Task<bool> TryRemoveWorker(string activityId, IWorkerInfo worker);

        /// <summary>
        /// ScaleManager requests to ping specific worker
        /// true or false return value indicates whether to keep or remove
        /// this worker from worker table
        /// </summary>
        Task<bool> PingWorker(string activityId, IWorkerInfo worker);
    }
}
