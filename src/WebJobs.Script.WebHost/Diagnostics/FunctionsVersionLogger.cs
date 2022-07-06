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
    public class FunctionsVersionLogger : IHostedService, IDisposable
    {
        private readonly ILogger _logger;
        private readonly Timer _timer;
        private readonly TimeSpan _interval;
        private readonly IEnvironment _environment;
        private bool _disposed;

        public FunctionsVersionLogger(ILoggerFactory loggerFactory, IEnvironment environment)
        {
            _logger = loggerFactory.CreateLogger<FunctionsVersionLogger>();
            _interval = TimeSpan.FromMinutes(10);
            _timer = new Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
            _environment = environment;
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
            _timer.Change(Timeout.Infinite, Timeout.Infinite);

            return Task.CompletedTask;
        }

        private async void OnTimer(object state)
        {
            if (true)
            {
                await PublishLogsSamplesAsync();
            }

            SetTimerInterval((int)_interval.TotalMilliseconds);
        }

        private Task PublishLogs()
        {
            try
            {
                if (_environment.IsKubernetesManagedHosting())
                {
                    _logger.LogInformation($"FUNCTIONS_EXTENSION_VERSION  : '{Environment.GetEnvironmentVariable("FUNCTIONS_EXTENSION_VERSION")}'");
                    _logger.LogInformation($"Framework : '{Environment.GetEnvironmentVariable("FRAMEWORK")}'");
                    _logger.LogInformation($"Framework version : '{Environment.GetEnvironmentVariable("FRAMEWORK_VERSION")}'");
                    _logger.LogInformation($"Slot : '{Environment.GetEnvironmentVariable("WEBSITE_SLOT_NAME")}'");
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
                _logger.LogError(ex, "Failed to publis logs");
            }
        }

        private void SetTimerInterval(int dueTime)
        {
            if (!_disposed)
            {
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
                    }
                }
            }
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _timer.Dispose();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
