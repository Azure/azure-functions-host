// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Config;

namespace Microsoft.Azure.WebJobs.Script
{
    public sealed class ScriptHostFactory : IScriptHostFactory
    {
        public ScriptHost Create(ScriptSettingsManager settingsManager, ScriptHostConfiguration config)
        {
            return ScriptHost.Create(config, settingsManager);
        }
    }
}
