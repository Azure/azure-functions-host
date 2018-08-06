// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script
{
    public class ConsoleScriptJobHostEnvironment : IScriptJobHostEnvironment
    {
        private IApplicationLifetime _applicationLifetime;

        public ConsoleScriptJobHostEnvironment(IApplicationLifetime applicationLifetime)
        {
            _applicationLifetime = applicationLifetime;
        }

        public void RestartHost()
        {
            _applicationLifetime.StopApplication();
        }

        public void Shutdown()
        {
            _applicationLifetime.StopApplication();
        }
    }
}
