using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal class BindingContext
    {
        private readonly FunctionBindingContext _functionContext;
        private readonly IReadOnlyDictionary<string, object> _bindingData;

        public BindingContext(FunctionBindingContext functionContext, IReadOnlyDictionary<string, object> bindingData)
        {
            _functionContext = functionContext;
            _bindingData = bindingData;
        }

        public FunctionBindingContext FunctionContext
        {
            get { return _functionContext; }
        }

        public IBindingProvider BindingProvider
        {
            get { return _functionContext.BindingProvider; }
        }

        public INotifyNewBlob NotifyNewBlob
        {
            get { return _functionContext.NotifyNewBlob; }
        }

        public CancellationToken CancellationToken
        {
            get { return _functionContext.CancellationToken; }
        }

        public INameResolver NameResolver
        {
            get { return _functionContext.NameResolver; }
        }

        public CloudStorageAccount StorageAccount
        {
            get { return _functionContext.StorageAccount; }
        }

        public string ServiceBusConnectionString
        {
            get { return _functionContext.ServiceBusConnectionString; }
        }

        public Guid FunctionInstanceId
        {
            get { return _functionContext.FunctionInstanceId; }
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
