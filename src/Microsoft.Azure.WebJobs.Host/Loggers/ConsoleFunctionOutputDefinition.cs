// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host.Loggers
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

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public Task<IFunctionOutput> CreateOutputAsync(CancellationToken cancellationToken)
        {
            IFunctionOutput output = new ConsoleFunctionOutputLog();
            return Task.FromResult(output);
        }

        public IRecurrentCommand CreateParameterLogUpdateCommand(IReadOnlyDictionary<string, IWatcher> watches,
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

            public IRecurrentCommand UpdateCommand
            {
                get { return null; }
            }

            public Task SaveAndCloseAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(0);
            }

            public void Dispose()
            {
            }
        }
    }
}
