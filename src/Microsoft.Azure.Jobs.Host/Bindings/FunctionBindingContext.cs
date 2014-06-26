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

        public FunctionBindingContext(HostBindingContext hostContext, Guid functionInstanceId, TextWriter consoleOutput)
        {
            _hostContext = hostContext;
            _functionInstanceId = functionInstanceId;
            _consoleOutput = consoleOutput;
        }

        public IBindingProvider BindingProvider
        {
            get { return _hostContext.BindingProvider; }
        }

        public CancellationToken CancellationToken
        {
            get { return _hostContext.CancellationToken; }
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
    }
}
