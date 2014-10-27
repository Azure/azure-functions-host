// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host.TestCommon
{
    public class NullFunctionOutputLoggerProvider : IFunctionOutputLoggerProvider
    {
        Task<IFunctionOutputLogger> IFunctionOutputLoggerProvider.GetAsync(CancellationToken cancellationToken)
        {
            IFunctionOutputLogger logger = new NullFunctionOutputLogger();
            return Task.FromResult(logger);
        }

        private class NullFunctionOutputLogger : IFunctionOutputLogger
        {
            public Task<IFunctionOutputDefinition> CreateAsync(IFunctionInstance instance,
                CancellationToken cancellationToken)
            {
                IFunctionOutputDefinition outputDefinition = new NullFunctionOutputDefinition();
                return Task.FromResult(outputDefinition);
            }
        }

        private class NullFunctionOutputDefinition : IFunctionOutputDefinition
        {
            public LocalBlobDescriptor OutputBlob
            {
                get { return null; }
            }

            public LocalBlobDescriptor ParameterLogBlob
            {
                get { return null; }
            }

            public Task<IFunctionOutput> CreateOutputAsync(CancellationToken cancellationToken)
            {
                IFunctionOutput output = new NullFunctionOutput();
                return Task.FromResult(output);
            }

            public IRecurrentCommand CreateParameterLogUpdateCommand(IReadOnlyDictionary<string, IWatcher> watches,
                TextWriter consoleOutput)
            {
                return null;
            }
        }

        private class NullFunctionOutput : IFunctionOutput
        {
            public IRecurrentCommand UpdateCommand
            {
                get { return null; }
            }

            public TextWriter Output
            {
                get { return TextWriter.Null; }
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
