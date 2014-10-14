// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Storage;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    internal class BindingProviderContext
    {
        private readonly INameResolver _nameResolver;
        private readonly IStorageAccount _storageAccount;
        private readonly string _serviceBusConnectionString;
        private readonly ParameterInfo _parameter;
        private readonly IReadOnlyDictionary<string, Type> _bindingDataContract;
        private readonly CancellationToken _cancellationToken;

        public BindingProviderContext(INameResolver nameResolver,
            IStorageAccount storageAccount,
            string serviceBusConnectionString,
            ParameterInfo parameter,
            IReadOnlyDictionary<string, Type> bindingDataContract,
            CancellationToken cancellationToken)
        {
            _nameResolver = nameResolver;
            _storageAccount = storageAccount;
            _serviceBusConnectionString = serviceBusConnectionString;
            _parameter = parameter;
            _bindingDataContract = bindingDataContract;
            _cancellationToken = cancellationToken;
        }

        public INameResolver NameResolver
        {
            get { return _nameResolver; }
        }

        public IStorageAccount StorageAccount
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

        public CancellationToken CancellationToken
        {
            get { return _cancellationToken; }
        }

        public string Resolve(string input)
        {
            if (_nameResolver == null)
            {
                return input;
            }

            return _nameResolver.ResolveWholeString(input);
        }

        public static BindingProviderContext Create(BindingContext bindingContext, ParameterInfo parameter,
            IReadOnlyDictionary<string, Type> bindingDataContract)
        {
            return new BindingProviderContext(bindingContext.NameResolver, bindingContext.StorageAccount,
                bindingContext.ServiceBusConnectionString, parameter, bindingDataContract,
                bindingContext.CancellationToken);
        }
    }
}
