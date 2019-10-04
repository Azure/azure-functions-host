// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class DefaultWorkerProcessFactory : IWorkerProcessFactory
    {
        public virtual Process CreateWorkerProcess(WorkerContext context)
        {
            var startInfo = new ProcessStartInfo(context.Arguments.ExecutablePath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false,
                ErrorDialog = false,
                WorkingDirectory = context.WorkingDirectory,
                Arguments = GetArguments(context),
            };

            return new Process { StartInfo = startInfo };
        }

        private StringBuilder MergeArguments(StringBuilder builder, string arg) => builder.AppendFormat(" {0}", arg);

        public string GetArguments(WorkerContext context)
        {
            var argumentsBuilder = context.Arguments.ExecutableArguments.Aggregate(new StringBuilder(), MergeArguments);
            if (!string.IsNullOrEmpty(context.Arguments.WorkerPath))
            {
                argumentsBuilder.AppendFormat(" \"{0}\"", context.Arguments.WorkerPath);
            }
            context.Arguments.WorkerArguments.Aggregate(argumentsBuilder, MergeArguments);
            argumentsBuilder.Append(context.GetFormatedArguments());
            return argumentsBuilder.ToString();
        }
    }
}
