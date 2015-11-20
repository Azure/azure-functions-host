// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    internal abstract class OutputBinding
    {
        public OutputBinding(string name, string type)
        {
            Name = name;
            Type = type;
        }

        public string Name { get; private set; }

        public string Type { get; private set; }

        public abstract bool HasBindingParameters { get; }

        public abstract Task BindAsync(IBinder binder, Stream stream, IReadOnlyDictionary<string, string> bindingData);

        internal static Collection<OutputBinding> GetOutputBindings(JArray outputs)
        {
            Collection<OutputBinding> outputBindings = new Collection<OutputBinding>();

            if (outputs != null)
            {
                foreach (var output in outputs)
                {
                    string type = (string)output["type"];
                    string name = (string)output["name"];

                    if (type == "blob")
                    {
                        string path = (string)output["path"];
                        outputBindings.Add(new BlobOutputBinding(name, path));
                    }
                    else if (type == "queue")
                    {
                        string queueName = (string)output["queueName"];
                        outputBindings.Add(new QueueOutputBinding(name, queueName));
                    }
                }
            }

            return outputBindings;
        }
    }
}
