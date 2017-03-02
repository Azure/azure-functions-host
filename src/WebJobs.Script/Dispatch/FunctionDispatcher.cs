// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    internal class FunctionDispatcher : IFunctionDispatcher
    {
        private IReadOnlyDictionary<ScriptType, ILanguageWorkerPool> _workerPools;

        public FunctionDispatcher()
        {
            _workerPools = new Dictionary<ScriptType, ILanguageWorkerPool>()
            {
                [ScriptType.Javascript] = new LanguageWorkerPool()
            };
        }

        public Task Initialize()
        {
            var startTasks = _workerPools.Select(pair => pair.Value.Start());
            return Task.WhenAll(startTasks);
        }

        public Task Register(FunctionMetadata functionMetadata)
        {
            return _workerPools[functionMetadata.ScriptType].Load(functionMetadata);
        }

        public Task<object> Invoke(FunctionMetadata functionMetadata, object[] parameters)
        {
            return _workerPools[functionMetadata.ScriptType].Invoke(functionMetadata, parameters);
        }
    }
}
