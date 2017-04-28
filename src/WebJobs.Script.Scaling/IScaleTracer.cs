// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Scaling
{
    public interface IScaleTracer
    {
        void TraceAddWorker(string activityId, IWorkerInfo workerInfo, string details);

        void TraceRemoveWorker(string activityId, IWorkerInfo workerInfo, string details);

        void TraceUpdateWorker(string activityId, IWorkerInfo workerInfo, string details);

        void TraceInformation(string activityId, IWorkerInfo workerInfo, string details);

        void TraceWarning(string activityId, IWorkerInfo workerInfo, string details);

        void TraceError(string activityId, IWorkerInfo workerInfo, string details);

        void TraceHttp(string activityId, IWorkerInfo workerInfo, string verb, string address, int statusCode, string startTime, string endTime, int latencyInMilliseconds, string requestContent, string details);
    }
}