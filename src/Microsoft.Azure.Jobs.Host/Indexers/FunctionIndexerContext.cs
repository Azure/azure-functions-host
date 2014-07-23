// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Indexers
{
    internal class FunctionIndexerContext
    {
        private readonly INameResolver _nameResolver;
        private readonly CloudStorageAccount _storageAccount;
        private readonly string _serviceBusConnectionString;
        private readonly ITriggerBindingProvider _triggerBindingProvider;
        private readonly IBindingProvider _bindingProvider;

        private FunctionIndexerContext(INameResolver nameResolver,
            CloudStorageAccount storageAccount,
            string serviceBusConnectionString,
            ITriggerBindingProvider triggerBindingProvider,
            IBindingProvider bindingProvider)
        {
            _nameResolver = nameResolver;
            _storageAccount = storageAccount;
            _serviceBusConnectionString = serviceBusConnectionString;
            _triggerBindingProvider = triggerBindingProvider;
            _bindingProvider = bindingProvider;
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

        public ITriggerBindingProvider TriggerBindingProvider
        {
            get { return _triggerBindingProvider; }
        }

        public IBindingProvider BindingProvider
        {
            get { return _bindingProvider; }
        }

        public string Resolve(string input)
        {
            if (_nameResolver == null)
            {
                return input;
            }

            return _nameResolver.ResolveWholeString(input);
        }

        public static FunctionIndexerContext CreateDefault(FunctionIndexContext indexContext,
            IEnumerable<Type> cloudBlobStreamBinderTypes)
        {
            return CreateDefault(indexContext.NameResolver, indexContext.StorageAccount,
                indexContext.ServiceBusConnectionString, cloudBlobStreamBinderTypes);
        }

        public static FunctionIndexerContext CreateDefault(INameResolver nameResolver,
            CloudStorageAccount storageAccount,
            string serviceBusConnectionString,
            IEnumerable<Type> cloudBlobStreamBinderTypes)
        {
            ITriggerBindingProvider triggerBindingProvider = DefaultTriggerBindingProvider.Create(cloudBlobStreamBinderTypes);
            IBindingProvider bindingProvider = DefaultBindingProvider.Create(cloudBlobStreamBinderTypes);
            return new FunctionIndexerContext(nameResolver, storageAccount, serviceBusConnectionString,
                triggerBindingProvider, bindingProvider);
        }
    }
}
