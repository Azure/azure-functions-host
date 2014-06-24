using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Indexers;
using Microsoft.Azure.Jobs.Host.Loggers;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Runners;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal class HostMessageExecutor : ITriggerExecutor<CloudQueueMessage>
    {
        private readonly IExecuteFunction _innerExecutor;
        private readonly IFunctionTableLookup _functionLookup;
        private readonly IFunctionInstanceLogger _functionInstanceLogger;
        private readonly RuntimeBindingProviderContext _context;

        public HostMessageExecutor(IExecuteFunction innerExecutor, IFunctionTableLookup functionLookup,
            IFunctionInstanceLogger functionInstanceLogger, RuntimeBindingProviderContext context)
        {
            _innerExecutor = innerExecutor;
            _functionLookup = functionLookup;
            _functionInstanceLogger = functionInstanceLogger;
            _context = context;
        }

        public bool Execute(CloudQueueMessage value)
        {
            HostMessage model = JsonCustom.DeserializeObject<HostMessage>(value.AsString);

            if (model == null)
            {
                throw new InvalidOperationException("Invalid invocation message.");
            }

            CallAndOverrideMessage callAndOverrideModel = model as CallAndOverrideMessage;

            if (callAndOverrideModel != null)
            {
                ProcessCallAndOverrideMessage(callAndOverrideModel, value.InsertionTime.Value, _context);
                return true;
            }

            AbortHostInstanceMessage abortModel = model as AbortHostInstanceMessage;

            if (abortModel != null)
            {
                ProcessAbortHostInstanceMessage();
                return true;
            }

            string error = String.Format(CultureInfo.InvariantCulture,
                "Unsupported invocation type '{0}'.", model.Type);
            throw new NotSupportedException(error);
        }

        // This snapshot won't contain full normal data for Function.FullName, Function.ShortName and Function.Parameters.
        // (All we know is an unavailable function ID; which function location method info to use is a mystery.)
        private static FunctionCompletedMessage CreateFailedMessage(CallAndOverrideMessage message, DateTimeOffset insertionType)
        {
            DateTimeOffset startAndEndTime = DateTimeOffset.UtcNow;

            // In theory, we could also set HostId, HostInstanceId and WebJobRunId; we'd just have to expose that data
            // directly to this Worker class.
            return new FunctionCompletedMessage
            {
                FunctionInstanceId = message.Id,
                Function = new FunctionDescriptor
                {
                    Id = message.FunctionId
                },
                Arguments = message.Arguments,
                ParentId = message.ParentId,
                Reason = message.Reason,
                StartTime = startAndEndTime,
                EndTime = startAndEndTime,
                Failure = new FunctionFailure
                {
                    ExceptionType = typeof(InvalidOperationException).FullName,
                    ExceptionDetails = String.Format(CultureInfo.CurrentCulture,
                        "No function '{0}' currently exists.", message.FunctionId)
                }
            };
        }

        private IFunctionInstance CreateFunctionInstance(CallAndOverrideMessage message, RuntimeBindingProviderContext context)
        {
            FunctionDefinition function = _functionLookup.Lookup(message.FunctionId);

            if (function == null)
            {
                return null;
            }

            IDictionary<string, object> objectParameters = new Dictionary<string, object>();

            if (message.Arguments != null)
            {
                foreach (KeyValuePair<string, string> item in message.Arguments)
                {
                    objectParameters.Add(item.Key, item.Value);
                }
            }

            return new FunctionInstance(message.Id, message.ParentId, message.Reason, new InvokeBindCommand(message.Id,
                function, objectParameters, context), function.Descriptor, function.Method);
        }

        private void ProcessCallAndOverrideMessage(CallAndOverrideMessage message, DateTimeOffset insertionTime,
            RuntimeBindingProviderContext context)
        {
            IFunctionInstance instance = CreateFunctionInstance(message, context);

            if (instance != null)
            {
                _innerExecutor.Execute(instance, context);
            }
            else
            {
                // Log that the function failed.
                FunctionCompletedMessage failedMessage = CreateFailedMessage(message, insertionTime);
                _functionInstanceLogger.LogFunctionCompleted(failedMessage);
            }
        }

        private static void ProcessAbortHostInstanceMessage()
        {
            bool terminated = NativeMethods.TerminateProcess(NativeMethods.GetCurrentProcess(), 1);
            Debug.Assert(terminated);
        }

    }
}
