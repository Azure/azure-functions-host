using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using RunnerInterfaces;

namespace Executor
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

    // Represents a single execution request. 
    internal class ExecutionInstance
    {    
        // Local directory where execution has been copied to.
        private readonly string _localCopy;

        // Capture output and logging. 
        private TextWriter _output;

        // localCopy - local directory where execution has been copied to.
        // Facilitates multiple instances sharing the same execution path.
        internal ExecutionInstance(string localCopy, TextWriter outputLogging)
        {
            _output = outputLogging;
            _localCopy = localCopy;
        }

        internal FunctionExecutionResult Execute(FunctionInvokeRequest instance, CancellationToken token)
        {
            Console.WriteLine("# Executing: {0}", instance.Location.GetId());
            
            // Log
            _output.WriteLine("Executing: {0}", instance.Location.GetId());
            foreach (var arg in instance.Args)
            {
                _output.WriteLine("  Arg:{0}", arg.ToString());
            }
            _output.WriteLine();

            var localInstance = ConvertToLocal(instance);

            var result = Utility.ProcessExecute<FunctionInvokeRequest, FunctionExecutionResult>(
                typeof(RunnerHost.Program),
                _localCopy,
                localInstance, _output,
                token);

            return result;
        }

        private FunctionInvokeRequest ConvertToLocal(FunctionInvokeRequest remoteFunc)
        {
            var remoteLoc = (RemoteFunctionLocation) remoteFunc.Location;

            var localLocation = remoteLoc.GetAsLocal(_localCopy);

            var localFunc = remoteFunc.CloneUpdateLocation(localLocation);            

            return localFunc;
        }
    }
}