// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.AspNetCore.Hosting;
using ExtensionsHostingEnvironment = Microsoft.Extensions.Hosting.IHostingEnvironment;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebScriptJobHostEnvironment : IScriptJobHostEnvironment
    {
        private readonly IApplicationLifetime _applicationLifetime;
        private readonly IScriptHostManager _hostManager;
        private readonly ExtensionsHostingEnvironment _hostingEnvironment;
        private int _shutdownRequested;
        private int _restartRequested;

        public WebScriptJobHostEnvironment(IApplicationLifetime applicationLifetime, IScriptHostManager hostManager, ExtensionsHostingEnvironment hostingEnvironment)
        {
            _applicationLifetime = applicationLifetime ?? throw new ArgumentNullException(nameof(applicationLifetime));
            _hostManager = hostManager ?? throw new ArgumentNullException(nameof(hostManager));
            _hostingEnvironment = hostingEnvironment ?? throw new ArgumentNullException(nameof(hostingEnvironment));
        }

        public string EnvironmentName => _hostingEnvironment.EnvironmentName;

        public void RestartHost()
        {
            if (_shutdownRequested == 0 && Interlocked.Exchange(ref _restartRequested, 1) == 0)
            {
                _hostManager.RestartHostAsync(CancellationToken.None);
            }
        }

        public void Shutdown(bool hard = false)
        {
            if (Interlocked.Exchange(ref _shutdownRequested, 1) == 0)
            {
                if (hard)
                {
                    // shut down the process
                    Process.GetCurrentProcess().Kill();
                }
                else
                {
                    // recycle the app domain
                    _applicationLifetime.StopApplication();
                }
            }
        }
    }
}
