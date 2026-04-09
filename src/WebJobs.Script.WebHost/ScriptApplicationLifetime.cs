// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    /// <summary>
    /// Default implementation of <see cref="IScriptApplicationLifetime"/> that delegates
    /// to the web host's <see cref="IHostApplicationLifetime"/>.
    /// </summary>
    internal sealed class ScriptApplicationLifetime : IScriptApplicationLifetime
    {
        private readonly IHostApplicationLifetime _hostApplicationLifetime;

        public ScriptApplicationLifetime(IHostApplicationLifetime hostApplicationLifetime)
        {
            _hostApplicationLifetime = hostApplicationLifetime ?? throw new ArgumentNullException(nameof(hostApplicationLifetime));
        }

        public CancellationToken ApplicationStarted => _hostApplicationLifetime.ApplicationStarted;

        public CancellationToken ApplicationStopping => _hostApplicationLifetime.ApplicationStopping;

        public CancellationToken ApplicationStopped => _hostApplicationLifetime.ApplicationStopped;

        public void StopApplication()
        {
            _hostApplicationLifetime.StopApplication();
        }
    }
}
