// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Logging
{
    public class ActivationEvent
    {
        public string ContainerName { get; set; }
        public long Start { get; set; } // time-bucket, per-minute.
        public long Length { get; set; } // # of units. 
    }

    public static class ActivationEventExtensions
    {
        public static DateTime GetStartTime(this ActivationEvent e)
        {
            return TimeBucket.ConveretToDateTime(e.Start);
        }
    }

    // A "container" refers to a single VM that's running functions. A container can be identified by the machine name.
    public interface ILogReader
    {
        // Get per-container timeline to tell how many active containers per minute. 
        // A container is "active" if it is running at least 1 function. 
        // Granularity here is 1 minute buckets. About 40,000 minutes per month. 
        //
        // Returns an array of events for which containers are active and for how long. 
        // This can be used to draw a histogram of usage for billing.         
        Task<ActivationEvent[]> GetActiveContainerCountOverTimeAsync(DateTime start, DateTime end);

        // Provides source of function Names
        // The names are needed to drill down in future queries. 
        Task<FunctionDefinitionEntity[]> GetFunctionDefinitionsAsync();


        // Drill down to function-instances of a given type within a timeline. 
        // To get total functions, must issue parallel queries for all function names (see GetFunctionDefinitionsAsync) 
        // stats will tell you TotalPass,Fail
        // Good for drawing a histogram. 
        Task<TimelineAggregateEntity[]> GetAggregateStatsAsync(string functionName, DateTime start, DateTime end);

    
        // Lookup an individual instance
        Task<InstanceTableEntity> LookupFunctionInstanceAsync(Guid functionInstanceId);

        // Get a query for recent function executions. 
        Task<IQueryResults<RecentPerFuncEntity>> GetRecentFunctionInstancesAsync(
            string functionName,
            bool onlyFailures = false // true to filter to ony failures
            );
    }

    // Execute the query 
    public interface IQueryResults<T>
    {
        // Get this many in the query 
        // a 0-length array means keep querying!
        // Returns null when completed. 
        Task<T[]> GetNextAsync(int limit);
    }    
}