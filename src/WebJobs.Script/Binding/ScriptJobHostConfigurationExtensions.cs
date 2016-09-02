// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Config;

namespace Microsoft.Azure.WebJobs.Script.Binding.Http
{
    public static class ScriptJobHostConfigurationExtensions
    {
        public static void UseScriptExtensions(this JobHostConfiguration config)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            config.RegisterExtensionConfigProvider(new ScriptExtensionConfig());
        }

        private class ScriptExtensionConfig : IExtensionConfigProvider
        {
            public void Initialize(ExtensionConfigContext context)
            {
                if (context == null)
                {
                    throw new ArgumentNullException("context");
                }

                context.Config.RegisterBindingExtension(new HttpTriggerAttributeBindingProvider());
                context.Config.RegisterBindingExtension(new ManualTriggerAttributeBindingProvider());
            }
        }
    }
}
