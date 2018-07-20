// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public static class ScriptJobHostConfigurationExtensions
    {
        public static void AddManualTrigger(this IHostBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            // TODO: DI (FACAVAL) Register the manual trigger

            //config.RegisterExtensionConfigProvider(new ScriptExtensionConfig());
        }

        private class ScriptExtensionConfig : IExtensionConfigProvider
        {
            public void Initialize(ExtensionConfigContext context)
            {
                if (context == null)
                {
                    throw new ArgumentNullException("context");
                }

                context.AddBindingRule<ManualTriggerAttribute>()
                    .BindToTrigger(new ManualTriggerAttributeBindingProvider());
            }
        }
    }
}
