using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Timers;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    internal interface IFunctionOutputDefinition
    {
        LocalBlobDescriptor OutputBlob { get; }

        LocalBlobDescriptor ParameterLogBlob { get; }

        IFunctionOutput CreateOutput();

        ICanFailCommand CreateParameterLogUpdateCommand(IReadOnlyDictionary<string, IWatcher> watches,
            TextWriter consoleOutput);
    }
}
