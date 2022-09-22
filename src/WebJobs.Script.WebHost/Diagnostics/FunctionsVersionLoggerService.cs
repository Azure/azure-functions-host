// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    /// <summary>
    /// Service responsible for logging versions
    /// </summary>
    public class FunctionsVersionLoggerService : IHostedService, IDisposable
    {
        private const double IntervalInSecondsDev = 1;    // interval for dev unit testing (1 s) in which the logs would be dumped
        private readonly ILogger _logger;
        private readonly IEnvironment _environment;
        private readonly Timer _timer;
        private readonly TimeSpan _interval;
        private bool _disposed;
        private double intervalInSecondsProd = 3600;      // default interval (1 hour) in which the logs would be dumped

        public FunctionsVersionLoggerService(ILogger<FunctionsVersionLoggerService> logger, IEnvironment environment)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _timer = new Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
            _interval = TimeSpan.FromSeconds(this.Interval);
        }

        public virtual double Interval
        {
            get { return (_environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSlotName) == "development") ? IntervalInSecondsDev : intervalInSecondsProd; }
            set { intervalInSecondsProd = value; }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // start the timer by setting the due time
            SetTimerInterval((int)_interval.TotalMilliseconds);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            // stop the timer if it has been started
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            return Task.CompletedTask;
        }

        private void OnTimer(object state)
        {
            PublishLogs();
        }

        private void PublishLogs()
        {
            try
            {
                _logger.LogInformation(
                    "FunctionsExtensionVersion = {FunctionsExtensionVersion},  Framework = {Framework}, FrameworkVersion = {FrameworkVersion}, SlotName = {SlotName}",
                    _environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsExtensionVersion),
                    _environment.GetEnvironmentVariable(EnvironmentSettingNames.Framework),
                    _environment.GetEnvironmentVariable(EnvironmentSettingNames.FrameworkVersion),
                    _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSlotName));
            }
            catch (Exception exc)
            {
                _logger.LogWarning(exc, "Failed to publish function version logs");
            }
        }

        private void SetTimerInterval(int dueTime)
        {
            if (_disposed || _timer is null)
            {
                return;
            }

            try
            {
                _timer.Change(0, dueTime);
            }
            catch (Exception exc)
            {
                // might race with dispose
                _logger.LogWarning(exc, "Unable to set timer interval");
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _timer?.Dispose();
            _disposed = true;
        }
    }
}
