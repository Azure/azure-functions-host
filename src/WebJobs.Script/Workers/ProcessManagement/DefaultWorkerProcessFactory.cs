// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    internal class DefaultWorkerProcessFactory : IWorkerProcessFactory
    {
        private ILogger _logger;

        public DefaultWorkerProcessFactory(ILoggerFactory loggerFactory)
        {
            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }
            _logger = loggerFactory.CreateLogger<DefaultWorkerProcessFactory>();
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
            var processEnvVariables = context.EnvironmentVariables;
            if (processEnvVariables != null && processEnvVariables.Any())
            {
                foreach (var envVar in processEnvVariables)
                {
                    startInfo.EnvironmentVariables[envVar.Key] = envVar.Value;
                    startInfo.Arguments = startInfo.Arguments.Replace($"%{envVar.Key}%", envVar.Value);
                }
            }
            return new Process { StartInfo = startInfo };
        }

        private StringBuilder MergeArguments(StringBuilder builder, string arg)
        {
            string expandedArg = Environment.ExpandEnvironmentVariables(arg);
            return builder.AppendFormat(" {0}", SanitizeExpandedArgument(expandedArg));
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
                _logger.LogWarning($"Environment variable:{match.Value} is not set");
                envExpandedString = envExpandedString.Replace(match.Value, string.Empty);
            }
            return envExpandedString;
        }
    }
}
