// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    internal abstract class Binding
    {
        public Binding(string name, string type, FileAccess fileAccess, bool isTrigger)
        {
            Name = name;
            Type = type;
            FileAccess = fileAccess;
            IsTrigger = isTrigger;
        }

        public string Name { get; private set; }

        public string Type { get; private set; }

        public bool IsTrigger { get; private set; }

        public FileAccess FileAccess { get; private set; }

        public abstract bool HasBindingParameters { get; }

        public abstract Task BindAsync(IBinder binder, Stream stream, IReadOnlyDictionary<string, string> bindingData);

        internal static Collection<Binding> GetBindings(JArray bindingArray, FileAccess fileAccess)
        {
            Collection<Binding> bindings = new Collection<Binding>();

            if (bindingArray != null)
            {
                foreach (var binding in bindingArray)
                {
                    string type = (string)binding["type"];
                    string name = (string)binding["name"];

                    if (type == "blob")
                    {
                        string path = (string)binding["path"];
                        bindings.Add(new BlobBinding(name, path, fileAccess, isTrigger: false));
                    }
                    else if (type == "blobTrigger")
                    {
                        string path = (string)binding["path"];
                        bindings.Add(new BlobBinding(name, path, fileAccess, isTrigger: true));
                    }
                    else if (type == "queue")
                    {
                        string queueName = (string)binding["queueName"];
                        bindings.Add(new QueueBinding(name, queueName, fileAccess, isTrigger: false));
                    }
                    else if (type == "queueTrigger")
                    {
                        string queueName = (string)binding["queueName"];
                        bindings.Add(new QueueBinding(name, queueName, fileAccess, isTrigger: true));
                    }
                }
            }

            return bindings;
        }
    }
}
