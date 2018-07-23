// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebScriptHostEnvironment : IScriptHostEnvironment
    {
        private readonly IApplicationLifetime _applicationLifetime;
        private readonly IScriptHostManager _hostManager;
        private int _shutdownRequested;
        private int _restartRequested;

        public WebScriptHostEnvironment(IApplicationLifetime applicationLifetime, IScriptHostManager hostManager)
        {
            _applicationLifetime = applicationLifetime ?? throw new ArgumentNullException(nameof(applicationLifetime));
            _hostManager = hostManager ?? throw new ArgumentNullException(nameof(hostManager));
        }

        public void RestartHost()
        {
            if (_shutdownRequested == 0 && Interlocked.Exchange(ref _restartRequested, 1) == 0)
            {
                _hostManager.RestartHostAsync(CancellationToken.None);
            }
        }

        public void Shutdown()
        {
            if (Interlocked.Exchange(ref _shutdownRequested, 1) == 0)
            {
                _applicationLifetime.StopApplication();
            }
        }
    }
}
