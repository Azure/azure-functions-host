// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Bindings.Runtime;
using Microsoft.Azure.WebJobs.Script.Binding;
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
                string logFilePath = Path.Combine(scriptConfig.RootLogPath, "Function", functionName);
                return new FileTraceWriter(logFilePath, TraceLevel.Verbose);
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
            TraceWriter.Error(error);

            // when any errors occur, we want to flush immediately
            TraceWriter.Flush();
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

        protected static Dictionary<string, string> GetBindingData(object value, IBinderEx binder, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            Dictionary<string, string> bindingData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // If there are any parameters in the bindings,
            // get the binding data. In dynamic script cases we need
            // to parse this POCO data ourselves - it won't be in the existing
            // binding data because all the POCO binders require strong
            // typing
            if (outputBindings.Any(p => p.HasBindingParameters) ||
                inputBindings.Any(p => p.HasBindingParameters))
            {
                // First apply any existing binding data. Any additional binding
                // data coming from the message will take precedence
                ApplyAmbientBindingData(binder, bindingData);

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
                            .Where(p => p.Value.Type != JTokenType.Object)
                            .ToDictionary(p => p.Name, p => (string)p);

                        if (additionalBindingData != null)
                        {
                            foreach (var item in additionalBindingData)
                            {
                                if (item.Value != null)
                                {
                                    bindingData[item.Key] = item.Value.ToString();
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

            return bindingData;
        }

        /// <summary>
        /// We need to merge the ambient binding data that already exists in the IBinder
        /// with our binding data. We have to do this rather than relying solely on
        /// IBinder.BindAsync because we need to include any POCO values we get from parsing
        /// JSON bodies, etc.
        /// TEMP: We might find a better way to do this in the future, perhaps via core
        /// SDK changes.
        /// </summary>
        protected static void ApplyAmbientBindingData(IBinderEx binder, IDictionary<string, string> bindingData)
        {
            var ambientBindingData = binder.BindingContext.BindingData;
            if (ambientBindingData != null)
            {
                // apply the binding data to ours
                foreach (var item in ambientBindingData)
                {
                    if (item.Value != null)
                    {
                        bindingData[item.Key] = item.Value.ToString();
                    }
                }
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
