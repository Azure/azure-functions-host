// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class SystemLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly string _hostInstanceId;
        private readonly IEventGenerator _eventGenerator;
        private readonly IEnvironment _environment;
        private readonly IDebugStateProvider _debugStateProvider;
        private readonly IScriptEventManager _eventManager;
        private readonly IOptionsMonitor<AppServiceOptions> _appServiceOptions;
        private IExternalScopeProvider _scopeProvider;

        public SystemLoggerProvider(IOptions<ScriptJobHostOptions> scriptOptions, IEventGenerator eventGenerator, IEnvironment environment, IDebugStateProvider debugStateProvider, IScriptEventManager eventManager, IOptionsMonitor<AppServiceOptions> appServiceOptions)
            : this(scriptOptions.Value.InstanceId, eventGenerator, environment, debugStateProvider, eventManager, appServiceOptions)
        {
        }

        protected SystemLoggerProvider(string hostInstanceId, IEventGenerator eventGenerator, IEnvironment environment, IDebugStateProvider debugStateProvider, IScriptEventManager eventManager, IOptionsMonitor<AppServiceOptions> appServiceOptions)
        {
            _eventGenerator = eventGenerator;
            _environment = environment;
            _hostInstanceId = hostInstanceId;
            _debugStateProvider = debugStateProvider;
            _eventManager = eventManager;
            _appServiceOptions = appServiceOptions;
        }

        public ILogger CreateLogger(string categoryName)
        {
            if (IsUserLogCategory(categoryName))
            {
                // The SystemLogger is not used for user logs.
                return NullLogger.Instance;
            }
            return new SystemLogger(_hostInstanceId, categoryName, _eventGenerator, _environment, _debugStateProvider, _eventManager, _scopeProvider, _appServiceOptions);
        }

        private bool IsUserLogCategory(string categoryName)
        {
            return LogCategories.IsFunctionUserCategory(categoryName) || categoryName.Equals(WorkerConstants.FunctionConsoleLogCategoryName, StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose()
        {
        }

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
        }
    }
}
