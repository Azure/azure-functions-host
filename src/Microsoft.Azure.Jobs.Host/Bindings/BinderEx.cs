using System;
using System.IO;
using System.Threading;

namespace Microsoft.Azure.Jobs
{
    internal class BinderEx : IBinderEx
    {
        private readonly IRuntimeBindingInputs _runtimeInputs;
        private readonly IConfiguration _config;
        private readonly Guid _FunctionInstanceGuid;
        private readonly TextWriter _consoleOutput;
        private readonly CancellationToken _cancellationToken;

        public BinderEx(IConfiguration config, IRuntimeBindingInputs runtimeInputs,
            Guid functionInstance, TextWriter consoleOutput, CancellationToken cancellationToken)
        {
            _config = config;
            _runtimeInputs = runtimeInputs;
            _FunctionInstanceGuid = functionInstance;
            _consoleOutput = consoleOutput;
            _cancellationToken = cancellationToken;
        }

        public string StorageConnectionString
        {
            get { return _runtimeInputs.StorageConnectionString; }
        }

        public CancellationToken CancellationToken
        {
            get { return _cancellationToken; }
        }

        public TextWriter ConsoleOutput
        {
            get { return _consoleOutput; }
        }
    }
}
