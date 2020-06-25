// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks.Dataflow;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    internal class WorkerConsoleLogSource : IWorkerConsoleLogSource
    {
        private readonly BufferBlock<ConsoleLog> _buffer = new BufferBlock<ConsoleLog>();

        public ISourceBlock<ConsoleLog> LogStream => _buffer;

        public void Log(ConsoleLog consoleLog)
        {
            _buffer.Post(consoleLog);
        }
    }
}
