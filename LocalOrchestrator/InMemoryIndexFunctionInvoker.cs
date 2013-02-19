using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Executor;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using RunnerHost;
using RunnerInterfaces;
using SimpleBatch;
using SimpleBatch.Client;

namespace Orchestrator
{
    class InMemoryIndexFunctionInvoker : LocalFunctionInvoker
    {
        private readonly IndexInMemory _indexer;

        public InMemoryIndexFunctionInvoker(IndexInMemory indexer)
            : base(indexer.Account, null)
        {
            // null scope means we don't invoke hooks.
            _indexer = indexer;
        }

        protected override MethodInfo ResolveMethod(string functionShortName)
        {
            return _indexer.GetMethod(functionShortName);
        }
    }
}