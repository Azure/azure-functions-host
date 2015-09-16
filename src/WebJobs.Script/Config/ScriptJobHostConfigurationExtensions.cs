// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using Microsoft.Azure.WebJobs.Script;

namespace Microsoft.Azure.WebJobs
{
    public static class ScriptJobHostConfigurationExtensions
    {
        public static void UseScripts(this JobHostConfiguration config, ScriptConfiguration scriptConfig)
        {
            FunctionDescriptionProvider[] descriptionProviders = new FunctionDescriptionProvider[]
            {
                new CSharpFunctionDescriptionProvider(scriptConfig.HostAssembly)
            };

            Collection<FunctionDescriptor> functions = Manifest.Read(scriptConfig, descriptionProviders);
            Type type = FunctionGenerator.Generate(functions);

            config.TypeLocator = new TypeLocator(type);

            config.UseWebHooks();
            config.UseTimers();
        }
    }
}
