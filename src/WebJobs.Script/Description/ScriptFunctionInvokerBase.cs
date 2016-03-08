// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public abstract class ScriptFunctionInvokerBase : IFunctionInvoker, IDisposable
    {
        private FileSystemWatcher _fileWatcher;
        private bool _disposed = false;

        internal ScriptFunctionInvokerBase(ScriptHost host, FunctionMetadata functionMetadata)
        {
            Host = host;
            Metadata = functionMetadata;
            TraceWriter = CreateTraceWriter(host.ScriptConfig, functionMetadata.Name);
        }

        public ScriptHost Host { get; private set; }

        public FunctionMetadata Metadata { get; private set; }

        public TraceWriter TraceWriter { get; private set; }

        private static TraceWriter CreateTraceWriter(ScriptHostConfiguration scriptConfig, string functionName)
        {
            if (scriptConfig.FileLoggingEnabled)
            {
                string logFilePath = Path.Combine(scriptConfig.RootLogPath, "Function", functionName);
                return new FileTraceWriter(logFilePath, TraceLevel.Verbose);
            }

            return NullTraceWriter.Instance;
        }

        protected void InitializeFileWatcherIfEnabled()
        {
            if (Host.ScriptConfig.FileWatchingEnabled)
            {
                string functionDirectory = Path.GetDirectoryName(Metadata.Source);
                _fileWatcher = new FileSystemWatcher(functionDirectory, "*.*")
                {
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };
                _fileWatcher.Changed += OnScriptFileChanged;
                _fileWatcher.Created += OnScriptFileChanged;
                _fileWatcher.Deleted += OnScriptFileChanged;
                _fileWatcher.Renamed += OnScriptFileChanged;
            }
        }

        public abstract Task Invoke(object[] parameters);

        protected virtual void OnScriptFileChanged(object sender, FileSystemEventArgs e)
        {
        }

        protected static Dictionary<string, string> GetBindingData(object value)
        {
            Dictionary<string, string> bindingData = new Dictionary<string, string>();

            try
            {
                // parse the object skipping any nested objects (binding data
                // only includes top level properties)
                JObject parsed = JObject.Parse(value as string);
                bindingData = parsed.Children<JProperty>()
                    .Where(p => p.Value.Type != JTokenType.Object)
                    .ToDictionary(p => p.Name, p => (string)p);
            }
            catch
            {
                // it's not an error if the incoming message isn't JSON
                // there are cases where there will be output binding parameters
                // that don't bind to JSON properties
            }

            return bindingData;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_fileWatcher != null)
                    {
                        _fileWatcher.Dispose();
                    }
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
