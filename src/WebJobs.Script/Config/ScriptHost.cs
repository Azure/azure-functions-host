// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script
{
    public class ScriptHost : JobHost
    {
        protected readonly JobHostConfiguration _config;
        protected readonly ScriptHostConfiguration _scriptConfig;

        protected ScriptHost(JobHostConfiguration config, ScriptHostConfiguration scriptConfig) 
            : base(config)
        {
            _config = config;
            _scriptConfig = scriptConfig;
        }

        protected virtual void Initialize()
        {
            List<FunctionDescriptorProvider> descriptionProviders = new List<FunctionDescriptorProvider>();
            descriptionProviders.Add(new ScriptFunctionDescriptorProvider(_scriptConfig.ApplicationRootPath));
            descriptionProviders.Add(new NodeFunctionDescriptorProvider(_scriptConfig.ApplicationRootPath));

            Manifest manifest = Manifest.Read(_scriptConfig, descriptionProviders);
            manifest.Apply(_config);
        }

        public static ScriptHost Create(ScriptHostConfiguration scriptConfig = null)
        {
            if (scriptConfig == null)
            {
                scriptConfig = new ScriptHostConfiguration()
                {
                    ApplicationRootPath = Environment.CurrentDirectory
                };
            }

            JobHostConfiguration config = new JobHostConfiguration();
            ScriptHost scriptHost = new ScriptHost(config, scriptConfig);
            scriptHost.Initialize();

            return scriptHost;
        }
    }
}
