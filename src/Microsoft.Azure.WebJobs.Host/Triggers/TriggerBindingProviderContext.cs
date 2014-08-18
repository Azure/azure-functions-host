// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Reflection;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Host.Triggers
{
    internal class TriggerBindingProviderContext
    {
        private readonly FunctionIndexerContext _indexerContext;
        private readonly ParameterInfo _parameter;
        private readonly CancellationToken _cancellationToken;

        public TriggerBindingProviderContext(FunctionIndexerContext indexerContext, ParameterInfo parameter,
            CancellationToken cancellationToken)
        {
            _indexerContext = indexerContext;
            _parameter = parameter;
            _cancellationToken = cancellationToken;
        }

        public INameResolver NameResolver
        {
            get { return _indexerContext.NameResolver; }
        }

        public CloudStorageAccount StorageAccount
        {
            get { return _indexerContext.StorageAccount; }
        }

        public string ServiceBusConnectionString
        {
            get { return _indexerContext.ServiceBusConnectionString; }
        }

        public ParameterInfo Parameter
        {
            get { return _parameter; }
        }

        public CancellationToken CancellationToken
        {
            get { return _cancellationToken; }
        }

        public string Resolve(string input)
        {
            return _indexerContext.Resolve(input);
        }
    }
}
