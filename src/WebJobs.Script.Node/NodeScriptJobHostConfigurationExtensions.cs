// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Node;

namespace Microsoft.Azure.WebJobs
{
    public static class NodeScriptJobHostConfigurationExtensions
    {
        public static void UseNodeScripts(this JobHostConfiguration config, ScriptConfiguration scriptConfig)
        {
            FunctionDescriptionProvider[] descriptionProviders = new FunctionDescriptionProvider[]
            {
                new NodeFunctionDescriptionProvider(scriptConfig.ApplicationRootPath)
            };

            Collection<FunctionDescriptor> functions = Manifest.Read(scriptConfig, descriptionProviders);
            Type type = FunctionGenerator.Generate(functions);

            config.TypeLocator = new TypeLocator(type);

            config.UseWebHooks();
            config.UseTimers();
        }
    }
}
