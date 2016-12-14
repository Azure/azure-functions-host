// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Dashboard.UnitTests.RestProtocol
{
    // Duplicate the models here. 
    // Specifically *don't* share the model class with the dashboard server code since we want to capture the exact binary serialization.
    // and catch any breaking changes. 
    // - don't use any JSON formatters since we don't want to hide casing changes. 
    // - Use string (and note Guid/DateTime) so we can catch exact format. 

    public class DashboardSegment<T>
    {
        public T[] entries { get; set; }
        public string continuationToken { get; set; }
    }

    public class FunctionStatisticsSegment
    {
        public IEnumerable<FunctionStatisticsViewModel> Entries { get; set; }
        public string ContinuationToken { get; set; }
        public DateTime? VersionUtc { get; set; }
    }

    public class FunctionStatisticsViewModel
    {
        public string functionId { get; set; }
        public string functionFullName { get; set; }
        public string functionName { get; set; }
        public int successCount { get; set; }
        public int failedCount { get; set; }
        public bool isRunning { get; set; }
    }

    public class FunctionInstanceDetailsViewModel
    {
        public InvocationLogViewModel Invocation { get; set; }

        //public ParameterModel[] Parameters { get; set; }

        public IEnumerable<Guid> ChildrenIds { get; set; }

        public InvocationLogViewModel Ancestor { get; set; }

        public TriggerReasonViewModel TriggerReason { get; set; }

        public string Trigger { get; set; }

        public bool IsAborted { get; set; }
    }

    public class TriggerReasonViewModel
    {
        public string parentGuid { get; set; }

        public string childGuid { get; set; }
    }

    public class InvocationLogViewModel
    {
        public string id { get; set; }
        public string functionId { get; set; }
        public string functionName { get; set; }
        public string functionFullName { get; set; }
        public string functionDisplayTitle { get; set; }
        public string status { get; set; }

        // Semantics of this change depending on status. 
        // If Running, it's STartTime. 
        // If completed, it's end time.
        public string whenUtc { get; set; }

        // Null if not completed yet. 
        public double? duration { get; set; }

        public string exceptionMessage { get; set; }
        public string exceptionType { get; set; }
    }

    class TimelineResponseEntry
    {
        public string Start { get; set; } // DateTime
        public int TotalPass { get; set; }
        public int TotalFail { get; set; }
        public int TotalRun { get; set; }
    }

}