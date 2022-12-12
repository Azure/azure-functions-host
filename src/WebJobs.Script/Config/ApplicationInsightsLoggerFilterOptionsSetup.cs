// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    internal class ApplicationInsightsLoggerFilterOptionsSetup : IConfigureOptions<LoggerFilterOptions>
    {
        private readonly IEnvironment _environment;

        internal static readonly AsyncLocal<bool> FilterApplicationInsightsFromWorker = new AsyncLocal<bool>();

        public ApplicationInsightsLoggerFilterOptionsSetup(IEnvironment environment)
        {
            _environment = environment;
        }

        public void Configure(LoggerFilterOptions options)
        {
            // Out-of-proc workers have the option of handling App Insights by themselves. If this is the case, they can
            // set this AsyncLocal to true to indicate the log should be skipped.
            if (!_environment.IsDotNetInProc())
            {
                options.AddFilter<ApplicationInsightsLoggerProvider>((_, _) => !FilterApplicationInsightsFromWorker.Value);
            }
        }
    }
}
