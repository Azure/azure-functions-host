// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Node
{
    public class NodeScriptHost : ScriptHost
    {
        private NodeScriptHost(JobHostConfiguration config, ScriptHostConfiguration scriptConfig)
            : base(config, scriptConfig)
        {
        }

        protected override IEnumerable<FunctionDescriptorProvider> GetFunctionDescriptionProviders()
        {
            return new FunctionDescriptorProvider[]
                {
                    new NodeFunctionDescriptorProvider(_scriptConfig.ApplicationRootPath)
                };
        }

        public static NodeScriptHost Create(ScriptHostConfiguration scriptConfig)
        {
            JobHostConfiguration config = new JobHostConfiguration();
            NodeScriptHost scriptHost = new NodeScriptHost(config, scriptConfig);
            scriptHost.Initialize();

            return scriptHost;
        }
    }
}
