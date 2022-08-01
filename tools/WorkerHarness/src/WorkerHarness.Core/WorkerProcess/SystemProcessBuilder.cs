// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using System.Text;
using WorkerHarness.Core.Commons;

namespace WorkerHarness.Core.WorkerProcess
{
    public class SystemProcessBuilder : IWorkerProcessBuilder
    {
        public IWorkerProcess Build(WorkerContext workerContext)
        {       
            var startInfo = new ProcessStartInfo(workerContext.ExecutablePath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false,
                ErrorDialog = false,
                WorkingDirectory = workerContext.WorkingDirectory,
                Arguments = GetArguments(workerContext)
            };

            Process process = new() { StartInfo = startInfo};

            return new SystemProcess(process);
        }

        private string GetArguments(WorkerContext context)
        {
            var argumentsBuilder = context.ExecutableArguments.Aggregate(new StringBuilder(), MergeArguments);
            if (!string.IsNullOrEmpty(context.WorkerPath))
            {
                argumentsBuilder.AppendFormat(" \"{0}\"", context.WorkerPath);
            }
            context.WorkerArguments.Aggregate(argumentsBuilder, MergeArguments);
            argumentsBuilder.Append(context.GetFormattedArguments());

            return argumentsBuilder.ToString();
        }

        private StringBuilder MergeArguments(StringBuilder builder, string arg)
        {
            string expandedArg = Environment.ExpandEnvironmentVariables(arg);

            return builder.AppendFormat(" {0}", expandedArg);
        }
    }
}
