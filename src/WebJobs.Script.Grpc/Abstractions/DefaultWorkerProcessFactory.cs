using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Abstractions.Rpc
{
    public class DefaultWorkerProcessFactory : IWorkerProcessFactory
    {

        public virtual Process CreateWorkerProcess(WorkerCreateContext context)
        {
            var startInfo = new ProcessStartInfo(context.WorkerConfig.ExecutablePath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false,
                ErrorDialog = false,
                WorkingDirectory = context.WorkingDirectory,
                Arguments = GetArguments(context),
            };

            context.Logger.LogInformation($"Exe: {context.WorkerConfig.ExecutablePath}");
            context.Logger.LogInformation($"Args: {startInfo.Arguments}");

            return new Process { StartInfo = startInfo };
        }

        private StringBuilder MergeArguments(StringBuilder builder, KeyValuePair<string, string> pair)
        {
            builder.AppendFormat("{0} {1} ", pair.Key, pair.Value);
            return builder;
        }

        public string GetArguments(WorkerCreateContext context)
        {
            var config = context.WorkerConfig;
            var argumentsBuilder = config.ExecutableArguments.Aggregate(new StringBuilder(), MergeArguments);
            argumentsBuilder.Append(config.WorkerPath);
            config.WorkerArguments.Aggregate(argumentsBuilder, MergeArguments);
            argumentsBuilder.AppendFormat(" --host {0} --port {1} --workerId {2} --requestId {3}",
                context.ServerUri.Host, context.ServerUri.Port, context.WorkerId, context.RequestId);
            return argumentsBuilder.ToString();
        }
    }
}
