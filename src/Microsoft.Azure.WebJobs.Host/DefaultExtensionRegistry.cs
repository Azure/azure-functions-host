// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Host.Config;

namespace Microsoft.Azure.WebJobs.Host
{
    internal class DefaultExtensionRegistry : IExtensionRegistry
    {
        private readonly JobHostMetadataProvider _tooling;

        private ConcurrentDictionary<Type, ConcurrentBag<object>> _registry = new ConcurrentDictionary<Type, ConcurrentBag<object>>();

        public DefaultExtensionRegistry(JobHostMetadataProvider tooling = null)
        {
            _tooling = tooling;
        }

        public void RegisterExtension(Type type, object instance)
        {
            if (type == null)
            {
                throw new ArgumentNullException("type");
            }
            if (instance == null)
            {
                throw new ArgumentNullException("instance");
            }
            if (!type.IsAssignableFrom(instance.GetType()))
            {
                throw new ArgumentOutOfRangeException("instance");
            }

            if (_tooling != null)
            {
                IExtensionConfigProvider extension = instance as IExtensionConfigProvider;
                if (extension != null)
                {
                    _tooling.AddExtension(extension);
                }
            }

            ConcurrentBag<object> instances = _registry.GetOrAdd(type, (t) => new ConcurrentBag<object>());
            instances.Add(instance);
        }

        public IEnumerable<object> GetExtensions(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException("type");
            }

            ConcurrentBag<object> instances = null;
            if (_registry.TryGetValue(type, out instances))
            {
                return instances;
            }

            return Enumerable.Empty<object>();
        }
    }
}
