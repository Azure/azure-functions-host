// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Config;

namespace Microsoft.Azure.WebJobs.Script
{
    public interface IScriptHostFactory
    {
        ScriptHost Create(IScriptHostEnvironment environment, ScriptSettingsManager settingsManager, ScriptHostConfiguration config);
    }
}
