using System;
using System.Diagnostics;

namespace Microsoft.Azure.Jobs
{
    // Move to extension methods so that serializers don't pick them up. 
    internal static class ExecutionInstanceLogEntityExtensions
    {
        public static FunctionInstanceStatus GetStatusWithoutHeartbeat(this ExecutionInstanceLogEntity obj)
        {
            return GetStatusWithHeartbeat(obj, null);
        }

        // beware, object may be partially filled out. So bias checks to fields that are present rather than missing. 
        public static FunctionInstanceStatus GetStatusWithHeartbeat(this ExecutionInstanceLogEntity obj, DateTime? heartbeatExpires)
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
            else if (heartbeatExpires.HasValue)
            {
                if (heartbeatExpires.Value < DateTime.UtcNow)
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
            var status = obj.GetStatusWithoutHeartbeat();
            return status.IsCompleted();
        }

        public static bool IsCompleted(this FunctionInstanceStatus status)
        {
            return status == FunctionInstanceStatus.CompletedSuccess || status == FunctionInstanceStatus.CompletedFailed;
        }
    }
}
