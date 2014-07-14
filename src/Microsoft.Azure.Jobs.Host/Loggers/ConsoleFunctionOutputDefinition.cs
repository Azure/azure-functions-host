using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Timers;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    internal sealed class ConsoleFunctionOutputDefinition : IFunctionOutputDefinition
    {
        public LocalBlobDescriptor OutputBlob
        {
            get { return null; }
        }

        public LocalBlobDescriptor ParameterLogBlob
        {
            get { return null; }
        }

        public IFunctionOutput CreateOutput()
        {
            return new ConsoleFunctionOutputLog();
        }

        public ICanFailCommand CreateParameterLogUpdateCommand(IReadOnlyDictionary<string, IWatcher> watches,
            TextWriter consoleOutput)
        {
            return null;
        }

        private sealed class ConsoleFunctionOutputLog : IFunctionOutput
        {
            public TextWriter Output
            {
                get { return Console.Out; }
            }

            public ICanFailCommand UpdateCommand
            {
                get { return null; }
            }

            public void SaveAndClose()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
