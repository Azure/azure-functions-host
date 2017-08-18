// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    // Each function can get its own log stream.
    // Static per-function logging information.
    public class FunctionLogger
    {
        public FunctionLogger(ScriptHost host, string functionName, string logDirName = null)
        {
            // Function file logging is only done conditionally
            TraceWriter traceWriter = host.FunctionTraceWriterFactory.Create(functionName, logDirName);
            FileTraceWriter = traceWriter.Conditional(t => host.FileLoggingEnabled && (!(t.Properties?.ContainsKey(ScriptConstants.TracePropertyPrimaryHostKey) ?? false) || host.IsPrimary));

            // The global trace writer used by the invoker will write all traces to both
            // the host trace writer as well as our file trace writer
            traceWriter = host.TraceWriter != null ?
                new CompositeTraceWriter(new TraceWriter[] { FileTraceWriter, host.TraceWriter }) :
                FileTraceWriter;

            // Apply the function name as an event property to all traces
            var functionTraceProperties = new Dictionary<string, object>
            {
                { ScriptConstants.TracePropertyFunctionNameKey, functionName }
            };

            TraceWriter = traceWriter.Apply(functionTraceProperties);
            Logger = host.ScriptConfig.HostConfig.LoggerFactory?.CreateLogger(LogCategories.Executor);
        }

        internal TraceWriter FileTraceWriter { get; private set; }

        public TraceWriter TraceWriter { get; private set; }

        public ILogger Logger { get; private set; }

        public void TraceError(string errorMessage)
        {
            TraceWriter.Error(errorMessage);
            Logger?.LogError(errorMessage);

            // when any errors occur, we want to flush immediately
            TraceWriter.Flush();
        }

        public TraceWriter CreateUserTraceWriter(TraceWriter traceWriter)
        {
            // We create a composite writer to ensure that all user traces get
            // written to both the original trace writer as well as our file trace writer
            // This is a "user" trace writer that will mark all traces going through
            // it as a "user" trace so they are filtered from system logs.
            var userTraceProperties = new Dictionary<string, object>
            {
                { ScriptConstants.TracePropertyIsUserTraceKey, true }
            };
            return new CompositeTraceWriter(new[] { traceWriter, FileTraceWriter }).Apply(userTraceProperties);
        }

        // Helper to emit a standard log message for function started.
        public void LogFunctionStart(string invocationId)
        {
            string startMessage = $"Function started (Id={invocationId})";
            TraceWriter.Info(startMessage);
            Logger?.LogInformation(startMessage);
        }

        public void LogFunctionResult(bool success, string invocationId, long elapsedMs)
        {
            string resultString = success ? "Success" : "Failure";
            string message = $"Function completed ({resultString}, Id={invocationId ?? "0"}, Duration={elapsedMs}ms)";

            TraceLevel traceWriterLevel = success ? TraceLevel.Info : TraceLevel.Error;
            LogLevel logLevel = success ? LogLevel.Information : LogLevel.Error;

            TraceWriter.Trace(message, traceWriterLevel, null);
            Logger?.Log(logLevel, new EventId(0), message, null, (s, e) => s);
        }
    }
}
