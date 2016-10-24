// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Logging
{  
    /// <summary>
    /// Read function activity.
    /// </summary>
    public interface ILogReader
    {
        /// <summary>
        /// Get the "# of function instance * size" for a given time window.
        /// Size is determined by the machine size. 
        /// </summary>
        /// <param name="startTime">start of time window</param>
        /// <param name="endTime">end of time window</param>
        /// <param name="numberBuckets">Size of the returned array</param>
        /// <returns>An array of (time, value) where value is the "# of function instance * size". 
        /// This can be readily graphed. </returns>
        Task<FunctionVolumeTimelineEntry[]> GetVolumeAsync(DateTime startTime, DateTime endTime, int numberBuckets);

        /// <summary>
        /// A "container" refers to a single VM that's running functions. A container can be identified by the machine name.
        /// Get per-container timeline to tell how many active containers per minute. 
        /// A container is "active" if it is running at least 1 function. 
        /// Granularity here is 1 minute buckets. About 40,000 minutes per month. 
        /// Returns an array of events for which containers are active and for how long. 
        /// This can be used to draw a histogram of usage for billing.         
        /// </summary>
        /// <param name="startTime">the inclusive start of the time window to query for.</param>
        /// <param name="endTime">the exclusive end of the time window to query for. </param>
        /// <param name="continuationToken"></param>
        /// <returns></returns>
        Task<Segment<ActivationEvent>> GetActiveContainerTimelineAsync(DateTime startTime, DateTime endTime, string continuationToken);

        /// <summary>
        /// Provides function definitions.
        /// The names are needed to drill down in future queries. 
        /// </summary>
        /// <param name="hostName">Host to get definitions for. If null, include all hosts.</param>
        /// <param name="continuationToken"></param>
        /// <returns>list of available function definitions.</returns>
        Task<Segment<IFunctionDefinition>> GetFunctionDefinitionsAsync(string hostName, string continuationToken);

        /// <summary>
        /// Drill down to function-instances of a given type within a timeline. 
        /// This returns a sparse array of entries. 
        /// To get total functions, must issue parallel queries for all function names (function names can be obtained from <see cref="GetFunctionDefinitionsAsync"/> ). 
        /// </summary>
        /// <param name="functionId">name of the function to query for.</param>
        /// <param name="startTime"></param>
        /// <param name="endTime"></param>
        /// <param name="continuationToken"></param>
        /// <returns></returns>
        Task<Segment<IAggregateEntry>> GetAggregateStatsAsync(FunctionId functionId, DateTime startTime, DateTime endTime, string continuationToken);

        /// <summary>
        /// Query recent function instances that match the given query parameters.
        /// </summary>
        /// <param name="query">parameters for what name, time window, etc to query on.</param>
        /// <param name="continuationToken">a token from a segment previously returned from this function.</param>
        /// <returns></returns>
        Task<Segment<IRecentFunctionEntry>> GetRecentFunctionInstancesAsync(
            RecentFunctionQuery query,
            string continuationToken
            );

        /// <summary>
        /// Lookup a specific function instance. The instances can be retrieved from <see cref="GetRecentFunctionInstancesAsync"/>
        /// </summary>
        /// <param name="functionInstanceId">function instance id that describes this specific instance. </param>
        /// <returns>null if the instance is not found.</returns>
        Task<FunctionInstanceLogItem> LookupFunctionInstanceAsync(Guid functionInstanceId);
    }   
}