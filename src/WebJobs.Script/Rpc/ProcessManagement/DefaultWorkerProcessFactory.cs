// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class DefaultWorkerProcessFactory : IWorkerProcessFactory
    {
        public virtual Process CreateWorkerProcess(WorkerCreateContext context)
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

        public string GetArguments(WorkerCreateContext context)
        {
            var argumentsBuilder = context.Arguments.ExecutableArguments.Aggregate(new StringBuilder(), MergeArguments);
            argumentsBuilder.AppendFormat(" \"{0}\"", context.Arguments.WorkerPath);
            context.Arguments.WorkerArguments.Aggregate(argumentsBuilder, MergeArguments);
            argumentsBuilder.AppendFormat(" --host {0} --port {1} --workerId {2} --requestId {3}",
                context.ServerUri.Host, context.ServerUri.Port, context.WorkerId, context.RequestId);
            return argumentsBuilder.ToString();
        }
    }
}
