// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public abstract class FunctionInvokerBase : IFunctionInvoker, IDisposable
    {
        private readonly ScriptJobHostOptions _scriptOptions;
        private readonly IScriptEventManager _eventManager;

        private bool _disposed = false;
        private IDisposable _fileChangeSubscription;

        internal FunctionInvokerBase(ScriptJobHostOptions scriptOptions, IScriptEventManager eventManager, FunctionMetadata functionMetadata, ILoggerFactory loggerFactory, string logDirName = null)
        {
            Metadata = functionMetadata;
            FunctionLogger = loggerFactory.CreateLogger(LogCategories.CreateFunctionCategory(functionMetadata.Name));
            _scriptOptions = scriptOptions;
            _eventManager = eventManager;
        }

        protected static IDictionary<string, object> PrimaryHostLogProperties { get; }
            = new ReadOnlyDictionary<string, object>(new Dictionary<string, object> { { ScriptConstants.LogPropertyPrimaryHostKey, true } });

        protected static IDictionary<string, object> PrimaryHostUserLogProperties { get; }
            = new ReadOnlyDictionary<string, object>(new Dictionary<string, object>(PrimaryHostLogProperties) { { ScriptConstants.LogPropertyIsUserLogKey, true } });

        protected static IDictionary<string, object> PrimaryHostSystemLogProperties { get; }
            = new ReadOnlyDictionary<string, object>(new Dictionary<string, object>(PrimaryHostLogProperties) { { ScriptConstants.LogPropertyIsSystemLogKey, true } });

        public ILogger FunctionLogger { get; }

        public FunctionMetadata Metadata { get; }

        /// <summary>
        /// All unhandled invocation exceptions will flow through this method.
        /// We format the error and write it to our function specific <see cref="TraceWriter"/>.
        /// </summary>
        /// <param name="ex">The exception instance.</param>
        public virtual void OnError(Exception ex)
        {
            string error = Utility.FlattenException(ex);

            TraceError(error);
        }

        protected virtual void TraceError(string errorMessage)
        {
            FunctionLogger.LogError(errorMessage);
        }

        protected bool InitializeFileWatcherIfEnabled()
        {
            if (_scriptOptions.FileWatchingEnabled)
            {
                string functionBasePath = Path.GetDirectoryName(Metadata.ScriptFile) + Path.DirectorySeparatorChar;
                _fileChangeSubscription = _eventManager.OfType<FileEvent>()
                    .Where(f => string.Equals(f.Source, EventSources.ScriptFiles, StringComparison.Ordinal) &&
                    f.FileChangeArguments.FullPath.StartsWith(functionBasePath, StringComparison.OrdinalIgnoreCase))
                    .Subscribe(e => OnScriptFileChanged(e.FileChangeArguments));

                return true;
            }

            return false;
        }

        public async Task<object> Invoke(object[] parameters)
        {
            FunctionInvocationContext context = GetContextFromParameters(parameters, Metadata);
            return await InvokeCore(parameters, context);
        }

        private static FunctionInvocationContext GetContextFromParameters(object[] parameters, FunctionMetadata metadata)
        {
            ExecutionContext functionExecutionContext = null;
            Binder binder = null;
            ILogger logger = null;

            for (var i = 0; i < parameters.Length; i++)
            {
                switch (parameters[i])
                {
                    case ExecutionContext fc:
                        functionExecutionContext ??= fc;
                        break;
                    case Binder b:
                        binder ??= b;
                        break;
                    case ILogger l:
                        logger ??= l;
                        break;
                }
            }

            // We require the ExecutionContext, so this will throw if one is not found.
            if (functionExecutionContext == null)
            {
                throw new ArgumentException("Function ExecutionContext was not found");
            }

            functionExecutionContext.FunctionDirectory = metadata.FunctionDirectory;
            functionExecutionContext.FunctionName = metadata.Name;

            FunctionInvocationContext context = new FunctionInvocationContext
            {
                ExecutionContext = functionExecutionContext,
                Binder = binder,
                Logger = logger
            };

            return context;
        }

        protected abstract Task<object> InvokeCore(object[] parameters, FunctionInvocationContext context);

        protected virtual void OnScriptFileChanged(FileSystemEventArgs e)
        {
        }

        protected internal void LogOnPrimaryHost(string message, LogLevel level, Exception exception = null)
        {
            IDictionary<string, object> properties = new Dictionary<string, object>(PrimaryHostLogProperties);

            FunctionLogger.Log(level, 0, properties, exception, (state, ex) => message);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _fileChangeSubscription?.Dispose();
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