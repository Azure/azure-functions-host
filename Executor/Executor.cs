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
    public class Executor : IDisposable
    {
        private readonly string _localCacheRoot;
        
        // Map from functionId to local cache.
        // Minimize re-downloading the same container to reexecute the same function 
        private Dictionary<string, ContainerDownloader> _localCacheMap = new Dictionary<string, ContainerDownloader>();

        public Executor(string localCache)
        {
            _localCacheRoot = Path.Combine(localCache, "daas-exec");
        }

        public void Execute(FunctionInstance instance)
        {
            Execute(instance, Console.Out, CancellationToken.None);
        }

        // Execute the function and block. 
        public FunctionExecutionResult Execute(FunctionInstance instance, TextWriter outputLogging, CancellationToken token)
        {
            string localCache = GetLocalCopy(instance.Location);

            ExecutionInstance i = new ExecutionInstance(localCache, outputLogging);
            return i.Execute(instance, token);
        }

        private string GetLocalCopy(FunctionLocation descr)
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
                download = new ContainerDownloader(descr.Blob, _localCacheRoot);
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
    public class ExecutionInstance
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

        internal FunctionExecutionResult Execute(FunctionInstance instance, CancellationToken token)
        {
            Console.WriteLine("# Executing: {0}", instance.Location.GetId());
            
            // Log
            _output.WriteLine("Executing: {0}", instance.Location.GetId());
            foreach (var arg in instance.Args)
            {
                _output.WriteLine("  Arg:{0}", arg.ToString());
            }
            _output.WriteLine();

            LocalFunctionInstance localInstance = ConvertToLocal(instance);

            var result = Utility.ProcessExecute<LocalFunctionInstance, FunctionExecutionResult>(
                typeof(RunnerHost.Program),
                _localCopy,
                localInstance, _output,
                token);

            return result;
        }

        // Copy a remote function instance to be local, in preparation for invoking. 
        private LocalFunctionInstance ConvertToLocal(FunctionInstance remoteInstance)
        {
            string assemblyEntryPoint = Path.Combine(_localCopy, remoteInstance.Location.Blob.BlobName);

            LocalFunctionInstance x = new LocalFunctionInstance
            {
                AssemblyPath = assemblyEntryPoint,
                TypeName = remoteInstance.Location.TypeName,
                MethodName = remoteInstance.Location.MethodName,
                Args = remoteInstance.Args,
                ParameterLogBlob = remoteInstance.ParameterLogBlob,
                Location = remoteInstance.Location,
                ServiceUrl = remoteInstance.ServiceUrl
            };

            return x;
        }
    }


  

   
}