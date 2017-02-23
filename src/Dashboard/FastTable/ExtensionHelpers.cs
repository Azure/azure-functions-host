// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Logging;

namespace Dashboard.Data
{
    internal static class ExtensionHelpers
    {
        public static FunctionInstanceSnapshot ConvertToSnapshot(this IRecentFunctionEntry entity)
        {
            return new FunctionInstanceSnapshot
            {
                Id = entity.FunctionInstanceId,
                FunctionFullName = entity.DisplayName,
                FunctionShortName = entity.DisplayName,
                DisplayTitle = entity.DisplayName, // skips Argument check
                StartTime = entity.StartTime,
                FunctionInstanceHeartbeatExpiry = entity.FunctionInstanceHeartbeatExpiry,
                Succeeded = entity.IsSucceeded(),
                EndTime = entity.EndTime
            };
        }

        public static FunctionInstanceSnapshot ConvertToSnapshot(this FunctionInstanceLogItem entity)
        {
            var arguments = new Dictionary<string, FunctionInstanceArgument>();
            foreach (var kv in entity.Arguments)
            {
                arguments[kv.Key] = new FunctionInstanceArgument { Value = kv.Value };
            }

            return new FunctionInstanceSnapshot
            {
                Id = entity.FunctionInstanceId,
                ParentId = entity.ParentId,
                FunctionFullName = entity.FunctionName,
                FunctionShortName = entity.FunctionName,

                StartTime = entity.StartTime,
                FunctionInstanceHeartbeatExpiry = entity.FunctionInstanceHeartbeatExpiry,
                EndTime = entity.EndTime,                

                Reason = entity.TriggerReason,
                InlineOutputText = entity.LogOutput,

                Succeeded = entity.IsSucceeded(),
                ExceptionMessage = entity.ErrorDetails,
                ExceptionType = "Failure", // generic failure message                

                Arguments = arguments
            };
        }
    }
}