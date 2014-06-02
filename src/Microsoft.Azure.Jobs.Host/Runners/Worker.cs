using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Blobs.Triggers;
using Microsoft.Azure.Jobs.Host.Loggers;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Queues.Triggers;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Runners
{
    internal class Worker
    {
        // Settings is for wiring up Azure endpoints for the distributed app.
        private readonly IFunctionTableLookup _functionTable;
        private readonly IExecuteFunction _executor;
        private readonly QueueTrigger _invokeTrigger;
        private readonly IFunctionInstanceLogger _functionInstanceLogger;
        private readonly INotifyNewBlob _notifyNewBlob;
        // Fast-path blob listener. 
        private readonly INotifyNewBlobListener _blobListener;

        // General purpose listener for blobs, queues. 
        private Listener _listener;


        public Worker(QueueTrigger invokeTrigger, IFunctionTableLookup functionTable, IExecuteFunction execute,
            IFunctionInstanceLogger functionInstanceLogger, INotifyNewBlob notifyNewBlob, INotifyNewBlobListener blobListener)
        {
            _invokeTrigger = invokeTrigger;
            if (functionTable == null)
            {
                throw new ArgumentNullException("functionTable");
            }
            if (execute == null)
            {
                throw new ArgumentNullException("execute");
            }
            _functionTable = functionTable;
            _executor = execute;
            _functionInstanceLogger = functionInstanceLogger;
            _notifyNewBlob = notifyNewBlob;
            _blobListener = blobListener;

            CreateInputMap();
        }

        // Called once at startup to initialize orchestration data structures
        // This is just retrieving the data structures created by the Indexer.
        private void CreateInputMap()
        {
            FunctionDefinition[] funcs = _functionTable.ReadAll();

            TriggerMap map = new TriggerMap();

            foreach (var func in funcs)
            {
                var ts = CalculateTriggers.GetTrigger(func);
                if (ts != null)
                {
                    map.AddTriggers(func.Location.GetId(), ts);
                }
            }

            if (_invokeTrigger != null)
            {
                map.AddTriggers(String.Empty, _invokeTrigger);
            }

            _listener = new Listener(map, new MyInvoker(this), this);
        }

        private int _triggerCount = 0;

        public void Poll(RuntimeBindingProviderContext context)
        {
            while (!context.CancellationToken.IsCancellationRequested)
            {
                int lastCount = _triggerCount;

                // this is a fast poll (checking a queue), so give it high priority
                PollNotifyNewBlobs(context);
                if (_triggerCount != lastCount)
                {
                    // This is a critical optimization.
                    // If a function writes a blob, immediately execute any functions that would
                    // have been triggered by that blob. Don't wait for a slow blob polling to detect it. 
                    continue;
                }

                _listener.Poll(context);

                if (_triggerCount != lastCount)
                {
                    continue;
                }
                break;
            }
        }

        public void StartPolling(RuntimeBindingProviderContext context)
        {
            _listener.StartPolling(context);
        }

        // Poll blob notifications from the fast path that may be detected ahead of our
        // normal listeners. 
        void PollNotifyNewBlobs(RuntimeBindingProviderContext context)
        {
            if (_blobListener != null)
            {
                _blobListener.ProcessMessages(this.NewBlob, context);
            }
        }

        // Called if the external system thinks we may have a new blob. 
        public void NewBlob(BlobWrittenMessage msg, RuntimeBindingProviderContext context)
        {
            _listener.InvokeTriggersForBlob(msg.AccountName, msg.ContainerName, msg.BlobName, context);
        }

        private void OnNewQueueItem(CloudQueueMessage msg, FunctionDefinition func, RuntimeBindingProviderContext context)
        {
            var instance = GetFunctionInvocation(func, context, msg);

            OnNewInvokeableItem(instance, context);
        }

        public void OnNewInvokeableItem(FunctionInvokeRequest instance, RuntimeBindingProviderContext context)
        {
            if (instance != null)
            {
                Interlocked.Increment(ref _triggerCount);
                _executor.Execute(instance, context);
            }
        }

        private void InvokeFromDashboard(CloudQueueMessage message, RuntimeBindingProviderContext context)
        {
            HostMessage model = JsonCustom.DeserializeObject<HostMessage>(message.AsString);

            if (model == null)
            {
                throw new InvalidOperationException("Invalid invocation message.");
            }

            TriggerAndOverrideMessage triggerOverrideModel = model as TriggerAndOverrideMessage;

            if (triggerOverrideModel != null)
            {
                FunctionInvokeRequest request = CreateInvokeRequest(triggerOverrideModel);

                if (request != null)
                {
                    _executor.Execute(request, context);
                }
                else
                {
                    // Log that the function failed.
                    FunctionCompletedSnapshot snapshot = CreateFailedSnapshot(triggerOverrideModel, message.InsertionTime.Value);
                    _functionInstanceLogger.LogFunctionCompleted(snapshot);
                }
            }
            else
            {
                string error = String.Format(CultureInfo.InvariantCulture, "Unsupported invocation type '{0}'.", model.Type);
                throw new NotSupportedException(error);
            }
        }

        // This snapshot won't contain full normal data for FunctionLongName and FunctionShortName.
        // (All we know is an unavailable function ID; which function location method info to use is a mystery.)
        private static FunctionCompletedSnapshot CreateFailedSnapshot(TriggerAndOverrideMessage message, DateTimeOffset insertionType)
        {
            DateTimeOffset startAndEndTime = DateTimeOffset.UtcNow;

            // In theory, we could also set HostId, HostInstanceId and WebJobRunId; we'd just have to expose that data
            // directly to this Worker class.
            return new FunctionCompletedSnapshot
            {
                FunctionInstanceId = message.Id,
                FunctionId = message.FunctionId,
                Arguments = CreateArguments(message.Arguments),
                ParentId = message.ParentId,
                Reason = message.Reason,
                StartTime = startAndEndTime,
                EndTime = startAndEndTime,
                Succeeded = false,
                ExceptionType = typeof(InvalidOperationException).FullName,
                ExceptionMessage = String.Format(CultureInfo.CurrentCulture,
                        "No function '{0}' currently exists.", message.FunctionId)
            };
        }

        private static IDictionary<string, FunctionArgument> CreateArguments(IDictionary<string, string> arguments)
        {
            IDictionary<string, FunctionArgument> returnValue = new Dictionary<string, FunctionArgument>();

            foreach (KeyValuePair<string, string> argument in arguments)
            {
                returnValue.Add(argument.Key, new FunctionArgument { Value = argument.Value });
            }

            return returnValue;
        }

        private FunctionInvokeRequest CreateInvokeRequest(TriggerAndOverrideMessage message)
        {
            FunctionDefinition function = _functionTable.Lookup(message.FunctionId);

            if (function == null)
            {
                return null;
            }

            FunctionInvokeRequest request = CreateInvokeRequest(function, message.Arguments, message.Id);
            request.TriggerReason = message.GetTriggerReason();
            return request;
        }

        internal static FunctionInvokeRequest CreateInvokeRequest(FunctionDefinition function, TriggerAndOverrideMessage message)
        {
            FunctionInvokeRequest request = CreateInvokeRequest(function, message.Arguments, message.Id);
            request.TriggerReason = message.GetTriggerReason();
            return request;
        }

        private static FunctionInvokeRequest CreateInvokeRequest(FunctionDefinition function, IDictionary<string, string> arguments, Guid id)
        {
            if (function == null)
            {
                throw new ArgumentNullException("function");
            }

            if (arguments == null)
            {
                throw new ArgumentNullException("arguments");
            }

            RuntimeBindingInputs inputs = new RuntimeBindingInputs(function.Location);
            ParameterRuntimeBinding[] boundArguments = new ParameterRuntimeBinding[function.Flow.Bindings.Length];

            for (int index = 0; index < boundArguments.Length; index++)
            {
                ParameterStaticBinding staticBinding = function.Flow.Bindings[index];
                string parameterName = staticBinding.Name;
                string value;

                if (!arguments.TryGetValue(parameterName, out value))
                {
                    value = null;
                }

                ParameterRuntimeBinding boundArgument;

                try
                {
                    boundArgument = staticBinding.BindFromInvokeString(inputs, value);
                }
                catch (Exception exception)
                {
                    boundArgument = new FailedParameterRuntimeBinding
                    {
                        BindingErrorMessage = exception.Message
                    };
                }

                boundArguments[index] = boundArgument;
            }

            return new FunctionInvokeRequest
            {
                Id = id,
                Location = function.Location,
                Args = boundArguments
            };
        }

        // Supports explicitly invoking any functions associated with this blob. 
        private void OnNewBlob(FunctionDefinition func, ICloudBlob blob, RuntimeBindingProviderContext context)
        {
            FunctionInvokeRequest instance = GetFunctionInvocation(func, context, blob);
            if (instance != null)
            {
                Interlocked.Increment(ref _triggerCount);
                _executor.Execute(instance, context);
            }
        }

        private static Guid GetBlobWriterGuid(ICloudBlob blob)
        {
            return BlobCausalityLogger.GetWriter(blob);
        }

        private static Guid GetOwnerFromMessage(CloudQueueMessage msg)
        {
            QueueCausalityHelper qcm = new QueueCausalityHelper();
            return qcm.GetOwner(msg);
        }

        public static FunctionInvokeRequest GetFunctionInvocation(
            FunctionDefinition func,
            IDictionary<string, string> parameters)
        {
            var ctx = new RuntimeBindingInputs(func.Location)
            {
                NameParameters = parameters
            };
            var instance = BindParameters(ctx, func, Guid.NewGuid());

            instance.NonTriggerBindings = func.NonTriggerBindings;

            return instance;
        }

        private FunctionInvokeRequest GetFunctionInvocation(FunctionDefinition func, RuntimeBindingProviderContext context, CloudQueueMessage msg)
        {
            Guid functionInstanceId = Guid.NewGuid();

            // Extract any named parameters from the queue payload.
            QueueTriggerBinding queueTriggerBinding = (QueueTriggerBinding)func.TriggerBinding;
            ITriggerData triggerData = queueTriggerBinding.Bind(msg,
                new ArgumentBindingContext
                {
                    FunctionInstanceId = functionInstanceId,
                    NotifyNewBlob = context.NotifyNewBlob,
                    CancellationToken = context.CancellationToken,
                    NameResolver = context.NameResolver,
                    StorageAccount = context.StorageAccount,
                    ServiceBusConnectionString = context.ServiceBusConnectionString,
                });
            IDictionary<string, string> p = GetNameParameters(triggerData.BindingData);

            // msg was the one that triggered it.
            RuntimeBindingInputs ctx = new RuntimeBindingInputs(func.Location)
            {
                NameParameters = p
            };

            var instance = BindParameters(ctx, func, functionInstanceId);

            instance.TriggerParameterName = func.TriggerParameterName;
            instance.TriggerData = triggerData;
            instance.NonTriggerBindings = func.NonTriggerBindings;

            instance.TriggerReason = new QueueMessageTriggerReason
            {
                QueueName = queueTriggerBinding.QueueName,
                MessageId = msg.Id,
                ParentGuid = GetOwnerFromMessage(msg)
            };

            return instance;
        }

        internal static IDictionary<string, string> GetNameParameters(IReadOnlyDictionary<string, object> bindingData)
        {
            if (bindingData == null)
            {
                return null;
            }

            Dictionary<string, string> nameParameters = new Dictionary<string, string>();

            if (bindingData != null)
            {
                foreach (KeyValuePair<string, object> item in bindingData)
                {
                    nameParameters.Add(item.Key, item.Value.ToString());
                }
            }

            return nameParameters;
        }

        internal static FunctionInvokeRequest GetFunctionInvocation(FunctionDefinition func, RuntimeBindingProviderContext context, ICloudBlob blobInput)
        {
            Guid functionInstanceId = Guid.NewGuid();

            // blobInput was the one that triggered it.
            BlobTriggerBinding blobTriggerBinding = func.TriggerBinding as BlobTriggerBinding;
            ITriggerData triggerData = blobTriggerBinding.Bind(blobInput,
                new ArgumentBindingContext
                {
                    FunctionInstanceId = functionInstanceId,
                    NotifyNewBlob = context.NotifyNewBlob,
                    CancellationToken = context.CancellationToken,
                    NameResolver = context.NameResolver,
                    StorageAccount = context.StorageAccount,
                    ServiceBusConnectionString = context.ServiceBusConnectionString,
                });

            // Get the binding data from the blob input parameter.
            IDictionary<string, string> p = GetNameParameters(triggerData.BindingData);

            if (p == null)
            {
                // No match.
                return null;
            }

            var ctx = new RuntimeBindingInputs(func.Location)
            {
                NameParameters = p
            };

            FunctionInvokeRequest instance = BindParameters(ctx, func, functionInstanceId);

            instance.TriggerParameterName = func.TriggerParameterName;
            instance.TriggerData = triggerData;
            instance.NonTriggerBindings = func.NonTriggerBindings;

            Guid parentGuid = GetBlobWriterGuid(blobInput);
            instance.TriggerReason = new BlobTriggerReason
            {
                BlobPath = new CloudBlobPath(blobInput),
                ParentGuid = parentGuid
            };

            return instance;
        }

        // Bind the entire flow to an instance
        public static FunctionInvokeRequest BindParameters(RuntimeBindingInputs ctx, FunctionDefinition func, Guid functionInstanceId)
        {
            FunctionFlow flow = func.Flow;
            int len = flow.Bindings.Length;

            var args = Array.ConvertAll(flow.Bindings, staticBinding => BindParameter(ctx, staticBinding));

            FunctionInvokeRequest instance = new FunctionInvokeRequest
            {
                Id = functionInstanceId,
                Location = func.Location,
                Args = args
            };

            return instance;
        }

        private static ParameterRuntimeBinding BindParameter(RuntimeBindingInputs ctx, ParameterStaticBinding staticBinding)
        {
            try
            {
                return staticBinding.Bind(ctx);
            }
            catch (InvalidOperationException ex)
            {
                return new FailedParameterRuntimeBinding { BindingErrorMessage = ex.Message };
            }
        }

        // plug into Trigger Service to queue invocations on triggers. 
        private class MyInvoker : ITriggerInvoke
        {
            private readonly Worker _parent;
            public MyInvoker(Worker parent)
            {
                _parent = parent;
            }

            void ITriggerInvoke.OnNewQueueItem(CloudQueueMessage msg, QueueTrigger trigger, RuntimeBindingProviderContext context)
            {
                FunctionDefinition func = (FunctionDefinition)trigger.Tag;

                if (func == null)
                {
                    _parent.InvokeFromDashboard(msg, context);
                }
                else
                {
                    _parent.OnNewQueueItem(msg, func, context);
                }
            }

            void ITriggerInvoke.OnNewBlob(ICloudBlob blob, BlobTrigger trigger, RuntimeBindingProviderContext context)
            {
                FunctionDefinition func = (FunctionDefinition)trigger.Tag;
                _parent.OnNewBlob(func, blob, context);
            }
        }
    }
}
