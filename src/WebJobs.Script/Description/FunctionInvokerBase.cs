// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Bindings.Runtime;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public abstract class FunctionInvokerBase : IFunctionInvoker, IDisposable
    {
        private FileSystemWatcher _fileWatcher;
        private bool _disposed = false;

        internal FunctionInvokerBase(ScriptHost host, FunctionMetadata functionMetadata)
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
                TraceLevel functionTraceLevel = scriptConfig.HostConfig.Tracing.ConsoleLevel;
                string logFilePath = Path.Combine(scriptConfig.RootLogPath, "Function", functionName);
                return new FileTraceWriter(logFilePath, functionTraceLevel);
            }

            return NullTraceWriter.Instance;
        }

        /// <summary>
        /// All unhandled invocation exceptions will flow through this method.
        /// We format the error and write it to our function specific <see cref="TraceWriter"/>.
        /// </summary>
        /// <param name="ex"></param>
        public virtual void OnError(Exception ex)
        {
            string error = Utility.FlattenException(ex);

            TraceError(error);
        }

        protected virtual void TraceError(string errorMessage)
        {
            TraceWriter.Error(errorMessage);

            // when any errors occur, we want to flush immediately
            TraceWriter.Flush();
        }

        protected bool InitializeFileWatcherIfEnabled()
        {
            if (Host.ScriptConfig.FileWatchingEnabled)
            {
                string functionDirectory = Path.GetDirectoryName(Metadata.ScriptFile);
                _fileWatcher = new FileSystemWatcher(functionDirectory, "*.*")
                {
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };
                _fileWatcher.Changed += OnScriptFileChanged;
                _fileWatcher.Created += OnScriptFileChanged;
                _fileWatcher.Deleted += OnScriptFileChanged;
                _fileWatcher.Renamed += OnScriptFileChanged;

                return true;
            }

            return false;
        }

        public abstract Task Invoke(object[] parameters);

        protected virtual void OnScriptFileChanged(object sender, FileSystemEventArgs e)
        {
        }

        /// <summary>
        /// Applies any additional binding data from the input value to the
        /// ambient binding context.
        /// </summary>
        internal static void ApplyBindingData(object value, Binder binder)
        {
            try
            {
                // if the input value is a JSON string, extract additional
                // binding data from it
                string json = value as string;
                if (!string.IsNullOrEmpty(json) && Utility.IsJson(json))
                {
                    // parse the object skipping any nested objects (binding data
                    // only includes top level properties)
                    JObject parsed = JObject.Parse(json);
                    var additionalBindingData = parsed.Children<JProperty>()
                        .Where(p => p.Value != null && 
                        (p.Value.Type != JTokenType.Object & p.Value.Type != JTokenType.Array))
                        .ToDictionary(p => p.Name, p => (string)p);

                    if (additionalBindingData != null)
                    {
                        foreach (var item in additionalBindingData)
                        {
                            if (item.Value != null)
                            {
                                binder.BindingData[item.Key] = item.Value;
                            }
                        }
                    }
                }
            }
            catch
            {
                // it's not an error if the incoming message isn't JSON
                // there are cases where there will be output binding parameters
                // that don't bind to JSON properties
            }
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

                    if (TraceWriter != null && TraceWriter is IDisposable)
                    {
                        ((IDisposable)TraceWriter).Dispose();
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
