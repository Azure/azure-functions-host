// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Logging
{
    /// <summary>
    /// Describe a function instance in the logs.  This is shared by both per-instance functions (which include more details) 
    ///  and the recent-function list. 
    /// </summary>
    public interface IFunctionInstanceBaseEntry
    {
        /// <summary>
        /// Unique instance id specifically for this instance of the function. 
        /// </summary>
        Guid FunctionInstanceId { get; }

        /// <summary>
        /// Name of the function, which can be used in further lookups about other instances of this function.
        /// For example, "Foo".
        /// </summary>
        string FunctionName { get; }

        /// <summary>
        /// If present, this is the UTC expiration time for the heartbeat. 
        /// If this is greater than the current time, then assume the function was abandoned (NeverFinished), 
        /// else assume the function is still running. 
        /// </summary>
        DateTime? FunctionInstanceHeartbeatExpiry { get; }

        /// <summary>UTC time that the function started executing.</summary>
        DateTime StartTime { get; }

        /// <summary>If set, the time the function completed (either successfully or failure). </summary>
        DateTime? EndTime { get;  }

        /// <summary>
        /// Function completed with error. 
        /// </summary>
        bool HasError { get; }
    }

    /// <summary>
    /// </summary>
    public static class IFunctionInstanceBaseEntryExtensions
    {
        /// <summary>
        /// Current status of this instance. 
        /// We specifically infer the status from the other fields rather than explicitly include status 
        /// so that we don't duplicate state. 
        /// </summary>
        public static FunctionInstanceStatus GetStatus(this IFunctionInstanceBaseEntry item)
        {
            if (item.IsCompleted())
            {
                if (item.IsSucceeded())
                {
                    return FunctionInstanceStatus.CompletedSuccess;
                }
                else
                {
                    return FunctionInstanceStatus.CompletedFailure;
                }
            }
            else
            {
                if (item.FunctionInstanceHeartbeatExpiry.HasValue)
                {
                    var now = DateTime.UtcNow;
                    bool isExpired = item.FunctionInstanceHeartbeatExpiry.Value < now;
                    if (isExpired)
                    {
                        return FunctionInstanceStatus.Abandoned;
                    }                    
                }
                return FunctionInstanceStatus.Running;                
            }
        }

        /// <summary>
        /// true if this function has completed (either success or failure)
        /// </summary>
        public static bool IsCompleted(this IFunctionInstanceBaseEntry item)
        {
            if (item == null)
            {
                throw new ArgumentNullException("item");
            }
            return item.EndTime.HasValue;
        }

        /// <summary>true if this function succeeded.</summary>
        public static bool IsSucceeded(this IFunctionInstanceBaseEntry item)
        {
            if (item == null)
            {
                throw new ArgumentNullException("item");
            }
            return item.IsCompleted() && !item.HasError;
        }
    }
}