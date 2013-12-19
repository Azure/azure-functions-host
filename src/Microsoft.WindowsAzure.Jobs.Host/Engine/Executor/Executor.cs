using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Microsoft.WindowsAzure.Jobs
{
    // Executor, can handle multiple execution requests.
    internal class Executor : IDisposable
    {
        private readonly string _localCacheRoot;
        
        // Map from functionId to local cache.
        // Minimize re-downloading the same container to reexecute the same function 
        private Dictionary<string, ContainerDownloader> _localCacheMap = new Dictionary<string, ContainerDownloader>();

        public Executor(string localCache)
        {
            _localCacheRoot = Path.Combine(localCache, "daas-exec");
        }

        public void Execute(FunctionInvokeRequest instance)
        {
            Execute(instance, Console.Out, CancellationToken.None);
        }

        // Execute the function and block. 
        public FunctionExecutionResult Execute(FunctionInvokeRequest instance, TextWriter outputLogging, CancellationToken token)
        {
            var remoteLoc = (RemoteFunctionLocation)instance.Location; // $$$ Handle other location types?
            string localCache = GetLocalCopy(remoteLoc);

            ExecutionInstance i = new ExecutionInstance(localCache, outputLogging);
            return i.Execute(instance, token);
        }

        private string GetLocalCopy(RemoteFunctionLocation descr)
        {
            string id = descr.GetId();
            ContainerDownloader download;
            if (!_localCacheMap.TryGetValue(id, out download))
            {
                // Add new one
                // $$$ Better policy to remove old ones?
                if (_localCacheMap.Count > 10)
                {
                    ClearCache();
                }
                download = new ContainerDownloader(descr.GetBlob(), _localCacheRoot);
                _localCacheMap[id] = download;
            }

            return download.LocalCachePrivate;
        }

        private void ClearCache()
        {
            // Nuke it!
            if (_localCacheMap != null)
            {
                foreach (var value in _localCacheMap.Values)
                {
                    value.Dispose();
                }
                _localCacheMap.Clear();
            }
        }

        public void Dispose()
        {
            ClearCache();
        }
    }
}
