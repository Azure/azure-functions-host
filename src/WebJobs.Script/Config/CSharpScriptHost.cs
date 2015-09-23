// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script
{
    public class CSharpScriptHost : ScriptHost
    {
        protected CSharpScriptHost(JobHostConfiguration config, ScriptHostConfiguration scriptConfig) 
            : base(config, scriptConfig)
        {
        }

        protected override IEnumerable<FunctionDescriptorProvider> GetFunctionDescriptionProviders()
        {
            return new FunctionDescriptorProvider[]
            {
                new CSharpFunctionDescriptorProvider(_scriptConfig.HostAssembly)
            };
        }

        public static CSharpScriptHost Create(ScriptHostConfiguration scriptConfig)
        {
            JobHostConfiguration config = new JobHostConfiguration();
            CSharpScriptHost scriptHost = new CSharpScriptHost(config, scriptConfig);
            scriptHost.Initialize();

            return scriptHost;
        }
    }
}
