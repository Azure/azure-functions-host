// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script
{
    public sealed class ScriptHostFactory : IScriptHostFactory
    {
        public ScriptHost Create(ScriptHostConfiguration config)
        {
            return ScriptHost.Create(config);
        }
    }
}
