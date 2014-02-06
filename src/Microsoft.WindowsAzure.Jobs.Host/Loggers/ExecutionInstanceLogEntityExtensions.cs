using System;
using System.Diagnostics;

namespace Microsoft.WindowsAzure.Jobs
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

        // Get a short summary string describing the queued, in-progress, completed info.
        public static string GetRunStatusString(this ExecutionInstanceLogEntity obj)
        {
            switch (obj.GetStatus())
            {
                case FunctionInstanceStatus.Running:
                    Debug.Assert(obj.StartTime.HasValue);
                    TimeSpan span = DateTime.UtcNow - obj.StartTime.Value;
                    return string.Format("Currently In-progress (for {0})", span);
                case FunctionInstanceStatus.CompletedSuccess:
                    return string.Format("Completed (at {0}, duration={1})", obj.EndTime, obj.GetDuration());
                case FunctionInstanceStatus.CompletedFailed:
                    return string.Format("Failed with exception: {1} (duration={0})", obj.GetDuration(), obj.ExceptionMessage);
                case FunctionInstanceStatus.NeverFinished:
                    return string.Format("Never finished (duration={0})", obj.GetDuration());
                default:
                    return "???";
            }
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
