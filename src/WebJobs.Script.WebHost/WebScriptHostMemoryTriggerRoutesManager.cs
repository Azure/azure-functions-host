// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    internal sealed partial class WebScriptHostMemoryTriggerRoutesManager : IMemoryTriggerRoutesManager
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly IEnvironment _environment;

        public WebScriptHostMemoryTriggerRoutesManager(ILoggerFactory loggerFactory, IEnvironment environment)
        {
            _loggerFactory = loggerFactory;
            _environment = environment;
        }

        public void InitializeMemoryTriggerFunctionRoutes(IScriptJobHost host)
        {
            TimeSpan delay = TimeSpan.FromSeconds(10);

            var routeHandler = new MemoryTriggerScriptRouteHandler(_loggerFactory, host, _environment, false);
            var listener = Task.Factory.StartNew(() =>
            {
                while (true)
                {
                    // TODO: Poll for memory changes;
                    Console.WriteLine("GOHAR!!!");
                    Thread.Sleep(delay);
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }
}
