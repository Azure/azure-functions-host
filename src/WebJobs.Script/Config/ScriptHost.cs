// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script
{
    public abstract class ScriptHost : JobHost
    {
        protected readonly JobHostConfiguration _config;
        protected readonly ScriptConfiguration _scriptConfig;

        protected ScriptHost(JobHostConfiguration config, ScriptConfiguration scriptConfig) 
            : base(config)
        {
            _config = config;
            _scriptConfig = scriptConfig;
        }

        protected abstract IEnumerable<FunctionDescriptorProvider> GetFunctionDescriptionProviders();

        protected virtual void Initialize()
        {
            IEnumerable<FunctionDescriptorProvider> descriptionProviders = GetFunctionDescriptionProviders();

            Manifest manifest = Manifest.Read(_scriptConfig, descriptionProviders);
            manifest.Apply(_config);

            _config.UseWebHooks();
            _config.UseTimers();
        }
    }
}
