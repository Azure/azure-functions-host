// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.Azure.WebJobs.Logging
{
    /// <summary>
    /// Status of a function instance 
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum FunctionInstanceStatus
    {
        /// <summary>
        /// Don't use. Serialization error. 
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// Function has been triggered and ready to run, but is blocked on some prerequeisite. 
        /// </summary>
        Blocked,

        /// <summary>
        /// Function is currently running. 
        /// </summary>
        Running,

        /// <summary>
        /// Function has completed successfully
        /// </summary>
        CompletedSuccess,

        /// <summary>
        /// Function completed with failure
        /// </summary>
        CompletedFailure,

        /// <summary>
        /// Function was aborted, such as an explicit cancel request from the user.  
        /// </summary>
        Aborted,

        /// <summary>
        /// The host abandoned the execution and won't resume this instance. 
        /// </summary>
        Abandoned
    }

    /// <summary>
    /// Extensions
    /// </summary>
    public static class FunctionStatusExtensions
    {
        /// <summary>
        /// Return true if the function has completed. This means its status won't change. 
        /// </summary>
        /// <param name="status"></param>
        /// <returns></returns>
        public static bool IsCompleted(this FunctionInstanceStatus status)
        {
            return (status == FunctionInstanceStatus.CompletedSuccess ||
                status == FunctionInstanceStatus.CompletedFailure ||
                status == FunctionInstanceStatus.Aborted ||
                status == FunctionInstanceStatus.Abandoned);
        }
    }
}