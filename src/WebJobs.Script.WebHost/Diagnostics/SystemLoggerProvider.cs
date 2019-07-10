// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Eventing;
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
        private IExternalScopeProvider _scopeProvider;
        private string[] _categoryPrefixes = new[] { "Host", "Function", "Worker" };

        public SystemLoggerProvider(IOptions<ScriptJobHostOptions> scriptOptions, IEventGenerator eventGenerator, IEnvironment environment, IDebugStateProvider debugStateProvider, IScriptEventManager eventManager)
            : this(scriptOptions.Value.InstanceId, eventGenerator, environment, debugStateProvider, eventManager)
        {
        }

        protected SystemLoggerProvider(string hostInstanceId, IEventGenerator eventGenerator, IEnvironment environment, IDebugStateProvider debugStateProvider, IScriptEventManager eventManager)
        {
            _eventGenerator = eventGenerator;
            _environment = environment;
            _hostInstanceId = hostInstanceId;
            _debugStateProvider = debugStateProvider;
            _eventManager = eventManager;
        }

        public ILogger CreateLogger(string categoryName)
        {
            // The SystemLogger is only for internal logs.
            if (_categoryPrefixes.Any(p => categoryName.StartsWith(p)) && !SystemLogger.IsAllowedCategory(categoryName))
            {
                return NullLogger.Instance;
            }

            return new SystemLogger(_hostInstanceId, categoryName, _eventGenerator, _environment, _debugStateProvider, _eventManager, _scopeProvider);
        }

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
        }

        public void Dispose()
        {
        }
    }
}
