// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Blobs;
using Microsoft.Azure.WebJobs.Host.Queues;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    internal class AmbientBindingContext
    {
        private readonly FunctionBindingContext _functionContext;
        private readonly IReadOnlyDictionary<string, object> _bindingData;

        public AmbientBindingContext(FunctionBindingContext functionContext,
            IReadOnlyDictionary<string, object> bindingData)
        {
            _functionContext = functionContext;
            _bindingData = bindingData;
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

        public IReadOnlyDictionary<string, object> BindingData
        {
            get { return _bindingData; }
        }
    }
}
