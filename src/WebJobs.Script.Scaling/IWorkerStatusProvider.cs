// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Scaling
{
    public interface IWorkerStatusProvider
    {
        /// <summary>
        /// this will be implemented by function runtime
        /// it will be called periodically to provide worker load factor.
        /// </summary>
        Task<int> GetWorkerStatus(string activityId);
    }
}
