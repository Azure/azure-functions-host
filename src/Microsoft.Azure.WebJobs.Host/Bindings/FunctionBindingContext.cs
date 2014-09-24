// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Blobs;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Storage;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    internal class FunctionBindingContext
    {
        private readonly HostBindingContext _hostContext;
        private readonly Guid _functionInstanceId;
        private readonly CancellationToken _functionCancellationToken;
        private readonly TextWriter _consoleOutput;

        public FunctionBindingContext(HostBindingContext hostContext, Guid functionInstanceId,
            CancellationToken functionCancellationToken, TextWriter consoleOutput)
        {
            _hostContext = hostContext;
            _functionInstanceId = functionInstanceId;
            _functionCancellationToken = functionCancellationToken;
            _consoleOutput = consoleOutput;
        }

        public IBindingProvider BindingProvider
        {
            get { return _hostContext.BindingProvider; }
        }

        public INameResolver NameResolver
        {
            get { return _hostContext.NameResolver; }
        }

        public IStorageAccount StorageAccount
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

        public IMessageEnqueuedWatcher MessageEnqueuedWatcher
        {
            get { return _hostContext.MessageEnqueuedWatcher; }
        }

        public Guid FunctionInstanceId
        {
            get { return _functionInstanceId; }
        }

        public CancellationToken FunctionCancellationToken
        {
            get { return _functionCancellationToken; }
        }

        public TextWriter ConsoleOutput
        {
            get { return _consoleOutput; }
        }
    }
}
