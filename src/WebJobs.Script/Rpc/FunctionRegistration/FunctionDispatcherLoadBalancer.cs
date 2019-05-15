// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class FunctionDispatcherLoadBalancer : IFunctionDispatcherLoadBalancer
    {
        private int _counter = 0;

        internal int Counter => _counter;

        public ILanguageWorkerChannel GetLanguageWorkerChannel(IEnumerable<ILanguageWorkerChannel> languageWorkerChannels, int maxProcessCount)
        {
            if (languageWorkerChannels == null)
            {
                throw new ArgumentNullException(nameof(languageWorkerChannels));
            }

            var currentNumberOfWorkers = languageWorkerChannels.Count();
            if (currentNumberOfWorkers == 0)
            {
                throw new InvalidOperationException($"Did not find any initialized language workers");
            }
            if (maxProcessCount == 1)
            {
                return languageWorkerChannels.FirstOrDefault();
            }
            var workerIndex = Interlocked.Increment(ref _counter) % currentNumberOfWorkers;
            if (_counter < 0 || workerIndex < 0)
            {
                _counter = 0;
                workerIndex = 0;
            }
            return languageWorkerChannels.ElementAt(workerIndex);
        }
    }
}
