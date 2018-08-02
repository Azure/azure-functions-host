﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class SystemLoggerProvider : ILoggerProvider
    {
        private readonly string _hostInstanceId;
        private IEventGenerator _eventGenerator;
        private ScriptSettingsManager _settingsManager;

        public SystemLoggerProvider(IOptions<ScriptHostOptions> scriptOptions, IEventGenerator eventGenerator, ScriptSettingsManager settingsManager)
        {
            _eventGenerator = eventGenerator;
            _settingsManager = settingsManager;
            _hostInstanceId = scriptOptions.Value.InstanceId;
        }

        public ILogger CreateLogger(string categoryName)
        {
            // The SystemLogger is not used for user logs.
            if (!LogCategories.IsFunctionUserCategory(categoryName))
            {
                return new SystemLogger(_hostInstanceId, categoryName, _eventGenerator, _settingsManager);
            }

            return NullLogger.Instance;
        }

        public void Dispose()
        {
        }
    }
}
