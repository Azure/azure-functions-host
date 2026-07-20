// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management.LinuxSpecialization
{
    public class BashCommandHandler : IBashCommandHandler
    {
        public const string FileCommand = "file";

        private readonly IMetricsLogger _metricsLogger;
        private readonly ILogger<BashCommandHandler> _logger;

        public BashCommandHandler(IMetricsLogger metricsLogger, ILogger<BashCommandHandler> logger)
        {
            _metricsLogger = metricsLogger ?? throw new ArgumentNullException(nameof(metricsLogger));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public (string Output, string Error, int ExitCode) RunCommand(string fileName, IReadOnlyList<string> arguments, string metricName)
        {
            try
            {
                using (_metricsLogger.LatencyEvent(metricName))
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = fileName,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    // Pass each argument separately via ArgumentList so they are never
                    // interpreted by a shell. This prevents command injection (CodeQL SM04899)
                    // when arguments are derived from untrusted input (e.g. WEBSITE_RUN_FROM_PACKAGE).
                    foreach (var argument in arguments)
                    {
                        process.StartInfo.ArgumentList.Add(argument);
                    }

                    _logger.LogInformation("Running: {FileName} (arguments omitted)", fileName);
                    process.Start();
                    var output = process.StandardOutput.ReadToEnd().Trim();
                    var error = process.StandardError.ReadToEnd().Trim();
                    process.WaitForExit();
                    _logger.LogInformation($"Output: {output}");
                    if (process.ExitCode != 0)
                    {
                        _logger.LogError(error);
                    }
                    else
                    {
                        _logger.LogInformation($"error: {error}");
                    }
                    _logger.LogInformation($"exitCode: {process.ExitCode}");
                    return (output, error, process.ExitCode);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error running command");
            }

            return (string.Empty, string.Empty, -1);
        }
    }
}
