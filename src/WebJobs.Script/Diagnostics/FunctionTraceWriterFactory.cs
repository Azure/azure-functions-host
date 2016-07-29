// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using System.IO;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script
{
    public sealed class FunctionTraceWriterFactory : ITraceWriterFactory
    {
        private readonly string _functionName;
        private readonly ScriptHostConfiguration _scriptHostConfig;

        public FunctionTraceWriterFactory(string functionName, ScriptHostConfiguration scriptHostConfig)
        {
            _functionName = functionName;
            _scriptHostConfig = scriptHostConfig;
        }

        public TraceWriter Create()
        {
            if (_scriptHostConfig.FileLoggingEnabled)
            {
                TraceLevel functionTraceLevel = _scriptHostConfig.HostConfig.Tracing.ConsoleLevel;
                string logFilePath = Path.Combine(_scriptHostConfig.RootLogPath, "Function", _functionName);
                return new FileTraceWriter(logFilePath, functionTraceLevel);
            }

            return NullTraceWriter.Instance;
        }
    }
}
