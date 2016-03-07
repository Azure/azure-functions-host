// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Logging;
using System.Collections.Generic;

namespace Dashboard.Data
{
    static class ExtensionHelpers
    {
        public static FunctionInstanceSnapshot ConvertToSnapshot(this RecentPerFuncEntity entity)
        {
            return new FunctionInstanceSnapshot
            {
                Id = entity.GetFunctionInstanceId(),
                FunctionFullName = entity.DisplayName,
                FunctionShortName = entity.DisplayName,
                DisplayTitle = entity.DisplayName, // skips Argument check
                StartTime = entity.StartTime,
                Succeeded = true, // Must be set to T/F if EndTime is set
                EndTime = entity.EndTime
            };
        }

        public static FunctionInstanceSnapshot ConvertToSnapshot(this InstanceTableEntity entity)
        {
            var arguments = new Dictionary<string, FunctionInstanceArgument>();
            foreach (var kv in entity.GetArguments())
            {
                arguments[kv.Key] = new FunctionInstanceArgument { Value = kv.Value };
            }

            return new FunctionInstanceSnapshot
            {
                Id = entity.GetFunctionInstanceId(),
                FunctionFullName = entity.FunctionName,
                FunctionShortName = entity.FunctionName,

                StartTime = entity.StartTime,
                EndTime = entity.EndTime,

                InlineOutputText = entity.LogOutput,

                Succeeded = entity.IsSucceeded(),
                ExceptionMessage = entity.ErrorDetails,

                Arguments = arguments
            };
        }
    }
}