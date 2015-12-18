// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    internal abstract class Binding
    {
        private readonly JobHostConfiguration _config;

        protected Binding(JobHostConfiguration config, string name, string type, FileAccess fileAccess, bool isTrigger)
        {
            _config = config;
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

        internal static Collection<Binding> GetBindings(JobHostConfiguration config, JArray bindingArray, FileAccess fileAccess)
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
                        bindings.Add(new BlobBinding(config, name, path, fileAccess, isTrigger: false));
                    }
                    else if (type == "blobTrigger")
                    {
                        string path = (string)binding["path"];
                        bindings.Add(new BlobBinding(config, name, path, fileAccess, isTrigger: true));
                    }
                    else if (type == "queue")
                    {
                        if (fileAccess != FileAccess.Write)
                        {
                            throw new InvalidOperationException("Queue binding can only be used for output.");
                        }
                        string queueName = (string)binding["queueName"];
                        bindings.Add(new QueueBinding(config, name, queueName, fileAccess, isTrigger: false));
                    }
                    else if (type == "serviceBus")
                    {
                        if (fileAccess != FileAccess.Write)
                        {
                            throw new InvalidOperationException("ServiceBus binding can only be used for output.");
                        }
                        string queueOrTopicName = (string)(binding["queueName"] ?? binding["topicName"]);
                        bindings.Add(new ServiceBusBinding(config, name, queueOrTopicName, fileAccess, isTrigger: false));
                    }
                    else if (type == "queueTrigger")
                    {
                        string queueName = (string)binding["queueName"];
                        bindings.Add(new QueueBinding(config, name, queueName, fileAccess, isTrigger: true));
                    }
                    else if (type == "table")
                    {
                        string tableName = (string)binding["tableName"];
                        string partitionKey = (string)binding["partitionKey"];
                        string rowKey = (string)binding["rowKey"];

                        TableQuery tableQuery = new TableQuery
                        {
                            TakeCount = (int?)binding["take"],
                            FilterString = (string)binding["filter"]
                        };

                        bindings.Add(new TableBinding(config, name, tableName, partitionKey, rowKey, fileAccess, tableQuery));
                    }
                }
            }

            return bindings;
        }

        protected string Resolve(string name)
        {
            if (_config.NameResolver == null)
            {
                return name;
            }

            return _config.NameResolver.ResolveWholeString(name);
        }
    }
}
