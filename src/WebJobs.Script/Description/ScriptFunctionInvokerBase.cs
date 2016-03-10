// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Binding;
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

        protected static Dictionary<string, string> GetBindingData(object value, IBinder binder, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            Dictionary<string, string> bindingData = new Dictionary<string, string>();

            // first apply any existing binding data
            ApplyAmbientBindingData(binder, bindingData);

            // If there are any parameters in the bindings,
            // get the binding data. In dynamic script cases we need
            // to parse this POCO data ourselves - it won't be in the existing
            // binding data because all the POCO binders require strong
            // typing
            if (outputBindings.Any(p => p.HasBindingParameters) ||
                inputBindings.Any(p => p.HasBindingParameters))
            {
                try
                {
                    string json = value as string;
                    if (!string.IsNullOrEmpty(json) && IsJson(json))
                    {
                        // parse the object skipping any nested objects (binding data
                        // only includes top level properties)
                        JObject parsed = JObject.Parse(json);
                        bindingData = parsed.Children<JProperty>()
                            .Where(p => p.Value.Type != JTokenType.Object)
                            .ToDictionary(p => p.Name, p => (string)p);
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
        /// TEMP HACK
        /// We need to merge the ambient binding data that already exists in the IBinder
        /// with our binding data. We have to do this rather than relying solely on
        /// IBinder.BindAsync because we need to include any POCO values we get from parsing
        /// JSON bodies, etc.
        /// </summary>
        protected static void ApplyAmbientBindingData(IBinder binder, IDictionary<string, string> bindingData)
        {
            // TEMP: Dig the ambient binding data out of the binder
            FieldInfo fieldInfo = binder.GetType().GetField("_bindingSource", BindingFlags.NonPublic | BindingFlags.Instance);
            var bindingSource = fieldInfo.GetValue(binder);
            PropertyInfo propertyInfo = bindingSource.GetType().GetProperty("AmbientBindingContext");
            var ambientBindingContext = propertyInfo.GetValue(bindingSource);
            propertyInfo = ambientBindingContext.GetType().GetProperty("BindingData");
            IDictionary<string, object> ambientBindingData = (IDictionary<string, object>)propertyInfo.GetValue(ambientBindingContext);

            if (ambientBindingData != null)
            {
                // apply the binding data to ours
                foreach (var item in ambientBindingData)
                {
                    bindingData[item.Key] = item.Value.ToString();
                }
            }
        }

        protected static bool IsJson(string input)
        {
            input = input.Trim();
            return (input.StartsWith("{", StringComparison.OrdinalIgnoreCase) && input.EndsWith("}", StringComparison.OrdinalIgnoreCase))
                || (input.StartsWith("[", StringComparison.OrdinalIgnoreCase) && input.EndsWith("]", StringComparison.OrdinalIgnoreCase));
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
