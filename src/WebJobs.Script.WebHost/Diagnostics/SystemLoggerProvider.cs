// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class SystemLoggerProvider : ILoggerProvider
    {
        private readonly string _hostInstanceId;
        private readonly IEventGenerator _eventGenerator;
        private readonly IEnvironment _environment;

        public SystemLoggerProvider(IOptions<ScriptJobHostOptions> scriptOptions, IEventGenerator eventGenerator, IEnvironment environment)
            : this(scriptOptions.Value.InstanceId, eventGenerator, environment)
        {
        }

        protected SystemLoggerProvider(string hostInstanceId, IEventGenerator eventGenerator, IEnvironment environment)
        {
            _eventGenerator = eventGenerator;
            _environment = environment;
            _hostInstanceId = hostInstanceId;
        }

        public ILogger CreateLogger(string categoryName)
        {
            // The SystemLogger is not used for user logs.
            if (IsUserLogCategory(categoryName))
            {
                return NullLogger.Instance;
            }
            return new SystemLogger(_hostInstanceId, categoryName, _eventGenerator, _environment);
        }

        private bool IsUserLogCategory(string categoryName)
        {
            return LogCategories.IsFunctionUserCategory(categoryName) || categoryName.Equals(LanguageWorkerConstants.FunctionConsoleLogCategoryName, StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose()
        {
        }
    }
}
