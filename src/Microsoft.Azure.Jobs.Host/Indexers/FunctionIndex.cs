// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Triggers;

namespace Microsoft.Azure.Jobs.Host.Indexers
{
    internal class FunctionIndex : IFunctionIndex
    {
        private readonly IBindingProvider _bindingProvider;
        private readonly IDictionary<string, IFunctionDefinition> _functionsById;
        private readonly IDictionary<MethodInfo, IFunctionDefinition> _functionsByMethod;
        private readonly ICollection<FunctionDescriptor> _functionDescriptors;

        private FunctionIndex(IBindingProvider bindingProvider)
        {
            _bindingProvider = bindingProvider;
            _functionsById = new Dictionary<string, IFunctionDefinition>();
            _functionsByMethod = new Dictionary<MethodInfo, IFunctionDefinition>();
            _functionDescriptors = new List<FunctionDescriptor>();
        }

        public IBindingProvider BindingProvider
        {
            get { return _bindingProvider; }
        }

        public static Task<FunctionIndex> CreateAsync(FunctionIndexContext context)
        {
            IEnumerable<Type> types = context.TypeLocator.GetTypes();
            IEnumerable<Type> cloudBlobStreamBinderTypes = GetCloudBlobStreamBinderTypes(types);
            return CreateAsync(context, types, cloudBlobStreamBinderTypes);
        }

        internal static async Task<FunctionIndex> CreateAsync(FunctionIndexContext context, IEnumerable<Type> types,
            IEnumerable<Type> cloudBlobStreamBinderTypes)
        {
            FunctionIndexerContext indexerContext = FunctionIndexerContext.CreateDefault(context,
                cloudBlobStreamBinderTypes);

            FunctionIndex index = new FunctionIndex(indexerContext.BindingProvider);
            FunctionIndexer indexer = new FunctionIndexer(indexerContext);

            foreach (Type type in types)
            {
                await indexer.IndexTypeAsync(type, index, context.CancellationToken);
            }

            return index;
        }

        public void Add(IFunctionDefinition function, FunctionDescriptor descriptor, MethodInfo method)
        {
            string id = descriptor.Id;

            if (_functionsById.ContainsKey(id))
            {
                throw new InvalidOperationException("Method overloads are not supported. " +
                    "There are multiple methods with the name '" + id + "'.");
            }

            _functionsById.Add(id, function);
            _functionsByMethod.Add(method, function);
            _functionDescriptors.Add(descriptor);
        }

        public IFunctionDefinition Lookup(string functionId)
        {
            if (!_functionsById.ContainsKey(functionId))
            {
                return null;
            }

            return _functionsById[functionId];
        }

        public IFunctionDefinition Lookup(MethodInfo method)
        {
            if (!_functionsByMethod.ContainsKey(method))
            {
                return null;
            }

            return _functionsByMethod[method];
        }

        public IEnumerable<IFunctionDefinition> ReadAll()
        {
            return _functionsById.Values;
        }

        public IEnumerable<FunctionDescriptor> ReadAllDescriptors()
        {
            return _functionDescriptors;
        }

        public IEnumerable<MethodInfo> ReadAllMethods()
        {
            return _functionsByMethod.Keys;
        }

        // Search for any types that implement ICloudBlobStreamBinder<T>
        internal static IEnumerable<Type> GetCloudBlobStreamBinderTypes(IEnumerable<Type> types)
        {
            List<Type> cloudBlobStreamBinderTypes = new List<Type>();

            foreach (Type type in types)
            {
                try
                {
                    foreach (Type interfaceType in type.GetInterfaces())
                    {
                        if (interfaceType.IsGenericType)
                        {
                            Type interfaceGenericDefinition = interfaceType.GetGenericTypeDefinition();

                            if (interfaceGenericDefinition == typeof(ICloudBlobStreamBinder<>))
                            {
                                cloudBlobStreamBinderTypes.Add(type);
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            return cloudBlobStreamBinderTypes;
        }
    }
}
