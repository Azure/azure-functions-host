// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Indexers
{
    internal class FunctionIndexContext
    {
        private readonly ITypeLocator _typeLocator;
        private readonly INameResolver _nameResolver;
        private readonly CloudStorageAccount _storageAccount;
        private readonly string _serviceBusConnectionString;
        private readonly CancellationToken _cancellationToken;

        public FunctionIndexContext(
            ITypeLocator typeLocator,
            INameResolver nameResolver,
            CloudStorageAccount storageAccount,
            string serviceBusConnectionString,
            CancellationToken cancellationToken)
        {
            _typeLocator = typeLocator;
            _nameResolver = nameResolver;
            _storageAccount = storageAccount;
            _serviceBusConnectionString = serviceBusConnectionString;
            _cancellationToken = cancellationToken;
        }

        public ITypeLocator TypeLocator
        {
            get { return _typeLocator; }
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
    }
}
