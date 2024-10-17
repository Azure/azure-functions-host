// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public interface IDeferredLogDispatcher
    {
        int Count { get; }

        bool IsEnabled { get; }

        void Log(DeferredLogEntry log);

        void AddLoggerProvider(ILoggerProvider provider);

        void ProcessBufferedLogs(bool runImmediately = false);
    }
}