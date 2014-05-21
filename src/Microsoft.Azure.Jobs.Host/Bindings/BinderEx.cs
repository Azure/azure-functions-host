using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace Microsoft.Azure.Jobs
{
    internal class BinderEx : IBinderEx
    {
        private readonly IRuntimeBindingInputs _runtimeInputs;
        private readonly IConfiguration _config;
        private readonly Guid _FunctionInstanceGuid;
        private readonly INotifyNewBlob _notificationService;
        private readonly TextWriter _consoleOutput;
        private readonly CancellationToken _cancellationToken;

        public BinderEx(IConfiguration config, IRuntimeBindingInputs runtimeInputs,
            Guid functionInstance, INotifyNewBlob notificationService, TextWriter consoleOutput,
            CancellationToken cancellationToken)
        {
            _config = config;
            _runtimeInputs = runtimeInputs;
            _FunctionInstanceGuid = functionInstance;
            _notificationService = notificationService;
            _consoleOutput = consoleOutput;
            _cancellationToken = cancellationToken;
        }

        // optionally pass in names, which flow to RuntimeBindingInputs?
        // names would just resolve against {} tokens in attributes?
        public BindResult<T> Bind<T>(Attribute attribute)
        {
            // Always bind as input parameters, eg no 'out' keyword. 
            // The binding could still have output semantics. Eg, bind to a TextWriter. 
            ParameterInfo p = new FakeParameterInfo(typeof(T), name: "?", isOut: false);

            // Same static binding as used in indexing
            ParameterStaticBinding staticBind = new StaticBinder(_config.NameResolver).DoStaticBind(attribute, p);

            // If somebody tried an non-sensical bind, we'd get the failure here 
            // here because the binding input doesn't have the information. 
            // Eg, eg a QueueInput attribute would fail because input doesn't have a queue input message.
            ParameterRuntimeBinding runtimeBind = staticBind.Bind(_runtimeInputs);

            BindResult result = runtimeBind.Bind(_config, this, p);
            return Utility.StrongWrapper<T>(result);
        }

        public string StorageConnectionString
        {
            get { return _runtimeInputs.StorageConnectionString; }
        }

        public CancellationToken CancellationToken
        {
            get { return _cancellationToken; }
        }

        public Guid FunctionInstanceGuid
        {
            get { return _FunctionInstanceGuid; }
        }

        public INotifyNewBlob NotifyNewBlob
        {
            get { return _notificationService; }
        }

        public TextWriter ConsoleOutput
        {
            get { return _consoleOutput; }
        }
    }
}
