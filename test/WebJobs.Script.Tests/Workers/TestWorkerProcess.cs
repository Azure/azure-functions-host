// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers
{
    internal class TestWorkerProcess : WorkerProcess
    {
        internal TestWorkerProcess(IScriptEventManager eventManager, IProcessRegistry processRegistry, ILogger workerProcessLogger, IWorkerConsoleLogSource consoleLogSource, IMetricsLogger metricsLogger, IServiceProvider serviceProvider, ILoggerFactory loggerFactory, IEnvironment environment, bool useStdErrStreamForErrorsOnly = false)
        : base(eventManager, processRegistry, workerProcessLogger, consoleLogSource, metricsLogger, serviceProvider, loggerFactory, environment, useStdErrStreamForErrorsOnly)
        {
        }

        internal override Process CreateWorkerProcess()
        {
            throw new NotImplementedException();
        }

        internal override void HandleWorkerProcessExitError(WorkerProcessExitException langExc)
        {
            throw new NotImplementedException();
        }

        internal override void HandleWorkerProcessRestart()
        {
            throw new NotImplementedException();
        }
    }
}
