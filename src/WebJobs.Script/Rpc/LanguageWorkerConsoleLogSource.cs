// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks.Dataflow;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class LanguageWorkerConsoleLogSource : ILanguageWorkerConsoleLogSource
    {
        private readonly BufferBlock<string> _buffer = new BufferBlock<string>();

        public ISourceBlock<string> LogStream => _buffer;

        public void Log(string consoleLog)
        {
            _buffer.Post(consoleLog);
        }
    }
}
