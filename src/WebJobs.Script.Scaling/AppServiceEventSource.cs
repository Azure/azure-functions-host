// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;

namespace Microsoft.Azure.WebJobs.Script.Scaling
{
    /// <summary>
    /// Provider guid is 3632bbab-9fb0-5519-11f1-a1cd6a645eb1
    /// </summary>
    [EventSource(Name = "Microsoft-Windows-WebSites-FunctionsScaleLogs")]
    [SuppressMessage("StyleCop.CSharp.NamingRules", "SA1313:ParameterNamesMustBeginWithLowerCaseLetter", Justification = "<Pending>", Scope = "member", Target = "~M:Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.EventGenerator.FunctionsSystemLogsEventSource.RaiseFunctionsEventWarning(System.String,System.String,System.String,System.String,System.String,System.String,System.String,System.String,System.String)")]
    public sealed class AppServiceEventSource : EventSource, IScaleTracer
    {
        [SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes", Justification = "By design")]
        public static readonly AppServiceEventSource Instance = new AppServiceEventSource();

        [Event(65300, Level = EventLevel.Informational, Channel = EventChannel.Operational, Version = 1)]
        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", Justification = "Need to use Pascal Case for MDS column names")]
        public void AddWorker(string SiteName, string StampName, string WorkerName, string Details)
        {
            if (IsEnabled())
            {
                WriteEvent(65300, SiteName, StampName, WorkerName, Details);
            }
        }

        [Event(65301, Level = EventLevel.Informational, Channel = EventChannel.Operational, Version = 1)]
        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", Justification = "Need to use Pascal Case for MDS column names")]
        public void RemoveWorker(string SiteName, string StampName, string WorkerName, string Details)
        {
            if (IsEnabled())
            {
                WriteEvent(65301, SiteName, StampName, WorkerName, Details);
            }
        }

        [Event(65302, Level = EventLevel.Informational, Channel = EventChannel.Operational, Version = 1)]
        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", Justification = "Need to use Pascal Case for MDS column names")]
        public void UpdateWorker(string SiteName, string StampName, string WorkerName, int LoadFactor, string Details)
        {
            if (IsEnabled())
            {
                WriteEvent(65302, SiteName, StampName, WorkerName, LoadFactor, Details);
            }
        }

        [Event(65303, Level = EventLevel.Informational, Channel = EventChannel.Operational, Version = 1)]
        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", Justification = "Need to use Pascal Case for MDS column names")]
        public void Information(string SiteName, string StampName, string WorkerName, string Details)
        {
            if (IsEnabled())
            {
                WriteEvent(65303, SiteName, StampName, WorkerName, Details);
            }
        }

        [Event(65304, Level = EventLevel.Warning, Channel = EventChannel.Operational, Version = 1)]
        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", Justification = "Need to use Pascal Case for MDS column names")]
        public void Warning(string SiteName, string StampName, string WorkerName, string Details)
        {
            if (IsEnabled())
            {
                WriteEvent(65304, SiteName, StampName, WorkerName, Details);
            }
        }

        [Event(65305, Level = EventLevel.Error, Channel = EventChannel.Operational, Version = 1)]
        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", Justification = "Need to use Pascal Case for MDS column names")]
        public void Error(string SiteName, string StampName, string WorkerName, string Details)
        {
            if (IsEnabled())
            {
                WriteEvent(65305, SiteName, StampName, WorkerName, Details);
            }
        }

        [Event(65306, Level = EventLevel.Informational, Channel = EventChannel.Operational, Version = 1)]
        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", Justification = "Need to use Pascal Case for MDS column names")]
        public void Http(string SiteName, string StampName, string WorkerName, string Verb, string Address, int StatusCode, string StartTime, string EndTime, int LatencyInMilliseconds, string RequestContent, string Details)
        {
            if (IsEnabled())
            {
                WriteEvent(65306, SiteName, StampName, WorkerName, Verb, Address, StatusCode, StartTime, EndTime, LatencyInMilliseconds, RequestContent, Details);
            }
        }

        void IScaleTracer.TraceAddWorker(string activityId, IWorkerInfo workerInfo, string details)
        {
            SetActivityId(activityId);
            AddWorker(workerInfo.SiteName, workerInfo.StampName, workerInfo.WorkerName, details);
        }

        void IScaleTracer.TraceRemoveWorker(string activityId, IWorkerInfo workerInfo, string details)
        {
            SetActivityId(activityId);
            RemoveWorker(workerInfo.SiteName, workerInfo.StampName, workerInfo.WorkerName, details);
        }

        void IScaleTracer.TraceUpdateWorker(string activityId, IWorkerInfo workerInfo, string details)
        {
            SetActivityId(activityId);
            UpdateWorker(workerInfo.SiteName, workerInfo.StampName, workerInfo.WorkerName, workerInfo.LoadFactor, details);
        }

        void IScaleTracer.TraceInformation(string activityId, IWorkerInfo workerInfo, string details)
        {
            SetActivityId(activityId);
            Information(workerInfo.SiteName, workerInfo.StampName, workerInfo.WorkerName, details);
        }

        void IScaleTracer.TraceWarning(string activityId, IWorkerInfo workerInfo, string details)
        {
            SetActivityId(activityId);
            Warning(workerInfo.SiteName, workerInfo.StampName, workerInfo.WorkerName, details);
        }

        void IScaleTracer.TraceError(string activityId, IWorkerInfo workerInfo, string details)
        {
            SetActivityId(activityId);
            Error(workerInfo.SiteName, workerInfo.StampName, workerInfo.WorkerName, details);
        }

        void IScaleTracer.TraceHttp(string activityId, IWorkerInfo workerInfo, string verb, string address, int statusCode, string startTime, string endTime, int latencyInMilliseconds, string requestContent, string details)
        {
            SetActivityId(activityId);
            Http(workerInfo.SiteName, workerInfo.StampName, workerInfo.WorkerName, verb, address, statusCode, startTime, endTime, latencyInMilliseconds, requestContent, details);
        }

        private static void SetActivityId(string activityId)
        {
            Guid guid;
            if (!string.IsNullOrEmpty(activityId) && Guid.TryParse(activityId, out guid))
            {
                SetCurrentThreadActivityId(guid);
            }
        }
    }
}