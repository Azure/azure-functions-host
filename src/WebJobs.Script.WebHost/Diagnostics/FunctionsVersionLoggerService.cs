// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly ILogger<FunctionsVersionLoggerService> _logger;
        private readonly Timer _timer;
        private readonly TimeSpan _interval;
        private readonly IEnvironment _environment;
        private bool _disposed;

        public FunctionsVersionLoggerService(ILogger<FunctionsVersionLoggerService> logger, IEnvironment environment)
        {
            _timer = new Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
            _interval = TimeSpan.FromMinutes(60);
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _logger = logger;
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

        private async void OnTimer(object state)
        {
            await PublishLogsSamplesAsync();
        }

        private Task PublishLogs()
        {
            try
            {
                if (_environment.IsKubernetesManagedHosting())
                {
                    _logger.LogInformation($"{EnvironmentSettingNames.FunctionsExtensionVersion}  : {_environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsExtensionVersion)}");
                    _logger.LogInformation($"{EnvironmentSettingNames.Framework} : {_environment.GetEnvironmentVariable(EnvironmentSettingNames.Framework)}");
                    _logger.LogInformation($"{EnvironmentSettingNames.FrameworkVersion}: {_environment.GetEnvironmentVariable(EnvironmentSettingNames.FrameworkVersion)}");
                    _logger.LogInformation($"{EnvironmentSettingNames.AzureWebsiteSlotName} : {_environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSlotName)}");
                }
                return Task.CompletedTask;
            }
            catch (Exception exc) when (!exc.IsFatal())
            {
                return null;
            }
        }

        private async Task PublishLogsSamplesAsync()
        {
            try
            {
                await PublishLogs();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish function version logs");
            }
        }

        private void SetTimerInterval(int dueTime)
        {
            if (_disposed)
            {
                return;
            }
            var timer = _timer;
            if (timer != null)
            {
                try
                {
                    _timer.Change(dueTime, Timeout.Infinite);
                }
                catch (ObjectDisposedException)
                {
                    // might race with dispose
                    _logger.LogWarning("Exception in SetTimerInterval");
                }
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
