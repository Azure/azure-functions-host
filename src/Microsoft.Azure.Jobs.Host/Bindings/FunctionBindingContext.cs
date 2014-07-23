// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Blobs;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal class FunctionBindingContext
    {
        private readonly HostBindingContext _hostContext;
        private readonly Guid _functionInstanceId;
        private readonly TextWriter _consoleOutput;
        private readonly CancellationToken _cancellationToken;

        public FunctionBindingContext(HostBindingContext hostContext, Guid functionInstanceId, TextWriter consoleOutput,
            CancellationToken cancellationToken)
        {
            _hostContext = hostContext;
            _functionInstanceId = functionInstanceId;
            _consoleOutput = consoleOutput;
            _cancellationToken = cancellationToken;
        }

        public IBindingProvider BindingProvider
        {
            get { return _hostContext.BindingProvider; }
        }

        public CancellationToken HostCancellationToken
        {
            get { return _hostContext.HostCancellationToken; }
        }

        public INameResolver NameResolver
        {
            get { return _hostContext.NameResolver; }
        }

        public CloudStorageAccount StorageAccount
        {
            get { return _hostContext.StorageAccount; }
        }

        public string ServiceBusConnectionString
        {
            get { return _hostContext.ServiceBusConnectionString; }
        }

        public IBlobWrittenWatcher BlobWrittenWatcher
        {
            get { return _hostContext.BlobWrittenWatcher; }
        }

        public Guid FunctionInstanceId
        {
            get { return _functionInstanceId; }
        }

        public TextWriter ConsoleOutput
        {
            get { return _consoleOutput; }
        }

        public CancellationToken CancellationToken
        {
            get { return _cancellationToken; }
        }
    }
}
