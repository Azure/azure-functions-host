using System;
using System.Diagnostics;

namespace Microsoft.Azure.Jobs
{
    // Move to extension methods so that serializers don't pick them up. 
    internal static class ExecutionInstanceLogEntityExtensions
    {
        // null if job hasn't finished yet.
        public static TimeSpan? GetDuration(this ExecutionInstanceLogEntity obj)
        {
            DateTime? endTime;

            if (obj.EndTime.HasValue)
            {
                endTime = obj.EndTime.Value;
            }
            else if (obj.HeartbeatExpires.HasValue)
            {
                endTime = obj.HeartbeatExpires.Value;
            }
            else
            {
                endTime = null;
            }

            if (!endTime.HasValue)
            {
                return null;
            }

            DateTime? startTime = obj.StartTime;

            if (!startTime.HasValue)
            {
                return null;
            }

            return endTime.Value - startTime.Value;
        }

        // beware, object may be partially filled out. So bias checks to fields that are present rather than missing. 
        public static FunctionInstanceStatus GetStatus(this ExecutionInstanceLogEntity obj)
        {
            if (obj.EndTime.HasValue)
            {
                if (obj.ExceptionType == null)
                {
                    return FunctionInstanceStatus.CompletedSuccess;
                }
                else
                {
                    return FunctionInstanceStatus.CompletedFailed;
                }
            }
            else if (obj.HeartbeatExpires.HasValue)
            {
                if (obj.HeartbeatExpires.Value < DateTime.UtcNow)
                {
                    return FunctionInstanceStatus.NeverFinished;
                }
                else
                {
                    return FunctionInstanceStatus.Running;
                }
            }
            else if (obj.StartTime.HasValue)
            {
                return FunctionInstanceStatus.Running;
            }
            else
            {
                return FunctionInstanceStatus.Queued;
            }
        }

        public static bool IsCompleted(this ExecutionInstanceLogEntity obj)
        {
            var status = obj.GetStatus();
            return status.IsCompleted();
        }

        public static bool IsCompleted(this FunctionInstanceStatus status)
        {
            return status == FunctionInstanceStatus.CompletedSuccess || status == FunctionInstanceStatus.CompletedFailed;
        }
    }
}
