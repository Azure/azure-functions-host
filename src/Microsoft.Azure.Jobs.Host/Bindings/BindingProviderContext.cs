// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Indexers;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal class BindingProviderContext
    {
        private readonly INameResolver _nameResolver;
        private readonly CloudStorageAccount _storageAccount;
        private readonly string _serviceBusConnectionString;
        private readonly ParameterInfo _parameter;
        private readonly IReadOnlyDictionary<string, Type> _bindingDataContract;

        public BindingProviderContext(INameResolver nameResolver, CloudStorageAccount storageAccount,
            string serviceBusConnectionString, ParameterInfo parameter,
            IReadOnlyDictionary<string, Type> bindingDataContract)
        {
            _nameResolver = nameResolver;
            _storageAccount = storageAccount;
            _serviceBusConnectionString = serviceBusConnectionString;
            _parameter = parameter;
            _bindingDataContract = bindingDataContract;
        }

        public INameResolver NameResolver
        {
            get { return _nameResolver; }
        }

        public CloudStorageAccount StorageAccount
        {
            get { return _storageAccount; }
        }

        public string ServiceBusConnectionString
        {
            get { return _serviceBusConnectionString; }
        }

        public ParameterInfo Parameter
        {
            get { return _parameter; }
        }

        public IReadOnlyDictionary<string, Type> BindingDataContract
        {
            get { return _bindingDataContract; }
        }

        public string Resolve(string input)
        {
            if (_nameResolver == null)
            {
                return input;
            }

            return _nameResolver.ResolveWholeString(input);
        }

        public static BindingProviderContext Create(FunctionIndexerContext indexerContext, ParameterInfo parameter,
            IReadOnlyDictionary<string, Type> bindingDataContract)
        {
            return new BindingProviderContext(indexerContext.NameResolver, indexerContext.StorageAccount,
                indexerContext.ServiceBusConnectionString, parameter, bindingDataContract);
        }

        public static BindingProviderContext Create(BindingContext bindingContext, ParameterInfo parameter,
            IReadOnlyDictionary<string, Type> bindingDataContract)
        {
            return new BindingProviderContext(bindingContext.NameResolver, bindingContext.StorageAccount,
                bindingContext.ServiceBusConnectionString, parameter, bindingDataContract);
        }
    }
}
