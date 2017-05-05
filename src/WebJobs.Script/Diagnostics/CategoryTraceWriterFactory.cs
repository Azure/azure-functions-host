// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using System.IO;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class CategoryTraceWriterFactory
    {
        private readonly string _category;
        private readonly ScriptHostConfiguration _scriptHostConfig;

        public CategoryTraceWriterFactory(string category, ScriptHostConfiguration scriptHostConfig)
        {
            _scriptHostConfig = scriptHostConfig;
            _category = category;
        }

        public TraceWriter Create()
        {
            if (_scriptHostConfig.FileLoggingMode != FileLoggingMode.Never)
            {
                TraceLevel functionTraceLevel = _scriptHostConfig.HostConfig.Tracing.ConsoleLevel;
                string logFilePath = Path.Combine(_scriptHostConfig.RootLogPath, _category);
                return new FileTraceWriter(logFilePath, functionTraceLevel);
            }

            return NullTraceWriter.Instance;
        }
    }
}
