// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Eventing;

namespace Microsoft.Azure.WebJobs.Script
{
    public sealed class ScriptHostFactory : IScriptHostFactory
    {
        public ScriptHost Create(
            IScriptHostEnvironment environment,
            IScriptEventManager eventManager,
            ScriptSettingsManager settingsManager,
            ScriptHostConfiguration config,
            ILoggerFactoryBuilder loggerFactoryBuilder)
        {
            return ScriptHost.Create(environment, eventManager, config, settingsManager, loggerFactoryBuilder);
        }
    }
}
