// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script
{
    public class ConsoleScriptJobHostEnvironment : IScriptJobHostEnvironment
    {
        private readonly IHostingEnvironment _hostingEnvironment;
        private IApplicationLifetime _applicationLifetime;

        public ConsoleScriptJobHostEnvironment(IApplicationLifetime applicationLifetime, IHostingEnvironment hostingEnvironment)
        {
            _applicationLifetime = applicationLifetime ?? throw new System.ArgumentNullException(nameof(applicationLifetime));
            _hostingEnvironment = hostingEnvironment ?? throw new System.ArgumentNullException(nameof(hostingEnvironment));
        }

        public string EnvironmentName => _hostingEnvironment.EnvironmentName;

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
