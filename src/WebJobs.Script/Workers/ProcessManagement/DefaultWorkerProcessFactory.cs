// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    internal class DefaultWorkerProcessFactory : IWorkerProcessFactory
    {
        private readonly ILogger _logger;
        private readonly IEnvironment _environment;

        public DefaultWorkerProcessFactory(IEnvironment environment, ILoggerFactory loggerFactory)
        {
            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }
            _logger = loggerFactory.CreateLogger<DefaultWorkerProcessFactory>();
            _environment = environment;
        }

        public virtual Process CreateWorkerProcess(WorkerContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (context.Arguments == null)
            {
                throw new ArgumentNullException(nameof(context.Arguments));
            }
            if (context.Arguments.ExecutablePath == null)
            {
                throw new ArgumentNullException(nameof(context.Arguments.ExecutablePath));
            }

            ProcessStartInfo startInfo = null;
            // In case of logic apps, We run exe's on linux using pal. So, Any .exe executions in linux and logic apps
            // should be run using pal.
            if (context is RpcWorkerContext rpcWorkerContext && rpcWorkerContext.PalEmulated == "true")
            {
                startInfo = new ProcessStartInfo("/palrun/bin/palrun")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    ErrorDialog = false,
                    WorkingDirectory = context.WorkingDirectory,
                    Arguments = GetPalRunArguments(rpcWorkerContext),
                };
            }
            else
            {
                startInfo = new ProcessStartInfo(context.Arguments.ExecutablePath)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    ErrorDialog = false,
                    WorkingDirectory = context.WorkingDirectory,
                    Arguments = GetArguments(context),
                };
            }

            var processEnvVariables = context.EnvironmentVariables;
            if (processEnvVariables != null && processEnvVariables.Any())
            {
                foreach (var envVar in processEnvVariables)
                {
                    startInfo.EnvironmentVariables[envVar.Key] = envVar.Value;
                    startInfo.FileName = startInfo.FileName.Replace($"%{envVar.Key}%", envVar.Value);
                    startInfo.Arguments = startInfo.Arguments.Replace($"%{envVar.Key}%", envVar.Value);
                }
                startInfo.Arguments = SanitizeExpandedArgument(startInfo.Arguments);
            }

            ApplyWorkerConcurrencyLimits(startInfo);

            return new Process { StartInfo = startInfo };
        }

        private StringBuilder MergeArguments(StringBuilder builder, string arg)
        {
            string expandedArg = Environment.ExpandEnvironmentVariables(arg);
            return builder.AppendFormat(" {0}", expandedArg);
        }

        private string GetPalRunArguments(RpcWorkerContext context)
        {
            var argumentsBuilder = new StringBuilder($"-p Package=\"{context.PalPackagePath}\" \"{context.PalExePath}\" -- ", 64);
            context.Arguments.ExecutableArguments.Aggregate(argumentsBuilder, MergeArguments);
            context.Arguments.WorkerArguments.Aggregate(argumentsBuilder, MergeArguments);
            argumentsBuilder.Append(context.GetFormattedArguments());
            _logger.LogInformation(argumentsBuilder.ToString());
            return argumentsBuilder.ToString();
        }

        public string GetArguments(WorkerContext context)
        {
            var argumentsBuilder = context.Arguments.ExecutableArguments.Aggregate(new StringBuilder(), MergeArguments);
            if (!string.IsNullOrEmpty(context.Arguments.WorkerPath))
            {
                argumentsBuilder.AppendFormat(" \"{0}\"", context.Arguments.WorkerPath);
            }
            context.Arguments.WorkerArguments.Aggregate(argumentsBuilder, MergeArguments);
            argumentsBuilder.Append(context.GetFormattedArguments());
            return argumentsBuilder.ToString();
        }

        internal string SanitizeExpandedArgument(string envExpandedString)
        {
            var regex = new Regex(@"%(.+?)%");
            var matches = regex.Matches(envExpandedString);
            foreach (Match match in matches)
            {
                _logger.LogDebug($"Environment variable:{match.Value} is not set");
                envExpandedString = envExpandedString.Replace(match.Value, string.Empty);
            }
            return envExpandedString;
        }

        internal void ApplyWorkerConcurrencyLimits(ProcessStartInfo startInfo)
        {
            string functionWorkerRuntime = startInfo.EnvironmentVariables.GetValueOrNull(RpcWorkerConstants.FunctionWorkerRuntimeSettingName);
            if (string.IsNullOrEmpty(startInfo.EnvironmentVariables.GetValueOrNull(RpcWorkerConstants.PythonThreadpoolThreadCount)) &&
                string.Equals(functionWorkerRuntime, RpcWorkerConstants.PythonLanguageWorkerName, StringComparison.OrdinalIgnoreCase))
            {
                startInfo.EnvironmentVariables[RpcWorkerConstants.PythonThreadpoolThreadCount] = RpcWorkerConstants.DefaultConcurrencyLimit;
            }
            else if (string.IsNullOrEmpty(startInfo.EnvironmentVariables.GetValueOrNull(RpcWorkerConstants.PSWorkerInProcConcurrencyUpperBound)) &&
               string.Equals(functionWorkerRuntime, RpcWorkerConstants.PowerShellLanguageWorkerName, StringComparison.OrdinalIgnoreCase))
            {
                startInfo.EnvironmentVariables[RpcWorkerConstants.PSWorkerInProcConcurrencyUpperBound] = RpcWorkerConstants.DefaultConcurrencyLimit;
            }
        }
    }
}
