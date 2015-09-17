// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class Manifest
    {
        public static Collection<FunctionDescriptor> Read(ScriptConfiguration config, IEnumerable<FunctionDescriptorProvider> descriptionProviders)
        {
            string manifestFilePath = Path.Combine(config.ApplicationRootPath, @"metadata\manifest.json");
            Console.WriteLine(string.Format("Reading job manifest file '{0}'", manifestFilePath));
            string json = File.ReadAllText(manifestFilePath);

            JObject manifest = JObject.Parse(json);
            JArray functionArray = (JArray)manifest["functions"];
            Collection<FunctionDescriptor> functions = new Collection<FunctionDescriptor>();
            foreach (JObject function in functionArray)
            {
                FunctionDescriptor descriptor = null;
                foreach (var provider in descriptionProviders)
                {
                    if (provider.TryCreate(function, out descriptor))
                    {
                        break;
                    }
                }

                if (descriptor != null)
                {
                    functions.Add(descriptor);
                }
            }

            return functions;
        }
    }
}
