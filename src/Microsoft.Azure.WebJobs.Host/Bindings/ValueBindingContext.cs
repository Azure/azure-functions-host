// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Blobs;
using Microsoft.Azure.WebJobs.Host.Queues;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    internal class ValueBindingContext
    {
        private readonly FunctionBindingContext _functionContext;
        private readonly CancellationToken _cancellationToken;

        public ValueBindingContext(FunctionBindingContext functionContext, CancellationToken cancellationToken)
        {
            _functionContext = functionContext;
            _cancellationToken = cancellationToken;
        }

        public FunctionBindingContext FunctionContext
        {
            get { return _functionContext; }
        }

        public Guid FunctionInstanceId
        {
            get { return _functionContext.FunctionInstanceId; }
        }

        public CancellationToken FunctionCancellationToken
        {
            get { return _functionContext.FunctionCancellationToken; }
        }

        public TextWriter ConsoleOutput
        {
            get { return _functionContext.ConsoleOutput; }
        }

        public CancellationToken CancellationToken
        {
            get { return _cancellationToken; }
        }
    }
}
