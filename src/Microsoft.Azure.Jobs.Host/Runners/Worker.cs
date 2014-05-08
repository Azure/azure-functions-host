using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs
{
    internal class Worker
    {
        // Settings is for wiring up Azure endpoints for the distributed app.
        private readonly IFunctionTableLookup _functionTable;
        private readonly IExecuteFunction _executor;
        private readonly QueueTrigger _invokeTrigger;
        private readonly IFunctionInstanceLogger _functionInstanceLogger;

        // General purpose listener for blobs, queues. 
        private Listener _listener;

        // Fast-path blob listener. 
        private INotifyNewBlobListener _blobListener;

        public Worker(QueueTrigger invokeTrigger, IFunctionTableLookup functionTable, IExecuteFunction execute,
            IFunctionInstanceLogger functionInstanceLogger, INotifyNewBlobListener blobListener = null)
        {
            _invokeTrigger = invokeTrigger;
            _blobListener = blobListener;
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

        public void Poll(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                int lastCount = _triggerCount;

                // this is a fast poll (checking a queue), so give it high priority
                PollNotifyNewBlobs(token);
                if (_triggerCount != lastCount)
                {
                    // This is a critical optimization.
                    // If a function writes a blob, immediately execute any functions that would
                    // have been triggered by that blob. Don't wait for a slow blob polling to detect it. 
                    continue;
                }

                _listener.Poll(token);

                if (_triggerCount != lastCount)
                {
                    continue;
                }
                break;
            }
        }

        // Poll blob notifications from the fast path that may be detected ahead of our
        // normal listeners. 
        void PollNotifyNewBlobs(CancellationToken token)
        {
            if (_blobListener != null)
            {
                _blobListener.ProcessMessages(this.NewBlob, token);
            }
        }

        // Called if the external system thinks we may have a new blob. 
        public void NewBlob(BlobWrittenMessage msg, CancellationToken cancellationToken)
        {
            _listener.InvokeTriggersForBlob(msg.AccountName, msg.ContainerName, msg.BlobName, cancellationToken);
        }

        private void OnNewQueueItem(CloudQueueMessage msg, FunctionDefinition func, CancellationToken cancellationToken)
        {
            var instance = GetFunctionInvocation(func, msg);

            OnNewInvokeableItem(instance, cancellationToken);
        }

        public void OnNewInvokeableItem(FunctionInvokeRequest instance, CancellationToken cancellationToken)
        {
            if (instance != null)
            {
                _triggerCount++;
                _executor.Execute(instance, cancellationToken);
            }
        }
        
        private void InvokeFromDashboard(CloudQueueMessage message, CancellationToken cancellationToken)
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
                    _executor.Execute(request, cancellationToken);
                }
                else
                {
                    // Log that the function failed.
                    request = CreateFailedInvokeRequest(triggerOverrideModel);
                    // In theory, we could also set HostInstanceId and ExecutingJobRunId; we'd just have to expose that data
                    // directly to this Worker class.
                    ExecutionInstanceLogEntity logEntity = new ExecutionInstanceLogEntity();
                    logEntity.FunctionInstance = request;
                    logEntity.QueueTime = message.InsertionTime.Value.UtcDateTime;
                    DateTime startAndEndTime = DateTime.UtcNow;
                    logEntity.StartTime = startAndEndTime;
                    logEntity.EndTime = startAndEndTime;
                    logEntity.ExceptionType = typeof(InvalidOperationException).FullName;
                    logEntity.ExceptionMessage = String.Format(CultureInfo.CurrentCulture,
                        "No function '{0}' currently exists.", triggerOverrideModel.FunctionId);
                    _functionInstanceLogger.LogFunctionCompleted(logEntity);
                }
            }
            else
            {
                string error = String.Format(CultureInfo.InvariantCulture, "Unsupported invocation type '{0}'.", model.Type);
                throw new NotSupportedException(error);
            }
        }

        // This function invoke request won't contain full normal data for FunctionLocation and Args/RuntimeBindings.
        // (All we know is an unavailable function ID; which function locatino method info and static bindings to use
        // for each parameter are a mystery).
        private static FunctionInvokeRequest CreateFailedInvokeRequest(TriggerAndOverrideMessage message)
        {
            ExecutionInstanceLogEntity logEntity = new ExecutionInstanceLogEntity();
            FunctionInvokeRequest request = new FunctionInvokeRequest();
            logEntity.FunctionInstance = request;

            request.Id = message.Id;
            request.TriggerReason = message.GetTriggerReason();
            request.Location = new PartialFunctionLocation(message.FunctionId);
            List<ParameterRuntimeBinding> arguments = new List<ParameterRuntimeBinding>();

            foreach (KeyValuePair<string, string> argument in message.Arguments)
            {
                arguments.Add(new PartialParameterRuntimeBinding(argument.Key, argument.Value));
            }

            request.Args = arguments.ToArray();
            return request;
        }

        private FunctionInvokeRequest CreateInvokeRequest(TriggerAndOverrideMessage message)
        {
            FunctionDefinition function = _functionTable.Lookup(message.FunctionId);

            if (function == null)
            {
                return null;
            }

            FunctionInvokeRequest request = CreateInvokeRequest(function, message.Arguments);
            request.Id = message.Id;
            request.TriggerReason = message.GetTriggerReason();
            return request;
        }

        internal static FunctionInvokeRequest CreateInvokeRequest(FunctionDefinition function, TriggerAndOverrideMessage message)
        {
            FunctionInvokeRequest request = CreateInvokeRequest(function, message.Arguments);
            request.Id = message.Id;
            request.TriggerReason = message.GetTriggerReason();
            return request;
        }

        private static FunctionInvokeRequest CreateInvokeRequest(FunctionDefinition function, IDictionary<string, string> arguments)
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
                function.Flow.GetInputParameters();
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
                Location = function.Location,
                Args = boundArguments
            };
        }

        // Supports explicitly invoking any functions associated with this blob. 
        private void OnNewBlob(FunctionDefinition func, ICloudBlob blob, CancellationToken cancellationToken)
        {
            FunctionInvokeRequest instance = GetFunctionInvocation(func, blob);
            if (instance != null)
            {
                _triggerCount++;
                _executor.Execute(instance, cancellationToken);
            }
        }

        private static Guid GetBlobWriterGuid(ICloudBlob blob)
        {
            IBlobCausalityLogger logger = new BlobCausalityLogger();
            return logger.GetWriter(blob);
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
            var instance = BindParameters(ctx, func);

            return instance;
        }

        // Invoke a function that is completely self-describing.
        // This means all inputs can be bound without any additional information. 
        // No reason set. 
        public static FunctionInvokeRequest GetFunctionInvocation(FunctionDefinition func)
        {
            var ctx = new RuntimeBindingInputs(func.Location);
            var instance = BindParameters(ctx, func);
            return instance;
        }

        public static FunctionInvokeRequest GetFunctionInvocation(FunctionDefinition func, CloudQueueMessage msg)
        {
            string payload = msg.AsString;

            // Extract any named parameters from the queue payload.
            var flow = func.Flow;
            QueueParameterStaticBinding qb = flow.Bindings.OfType<QueueParameterStaticBinding>().Where(b => b.IsInput).First();
            IDictionary<string, string> p = QueueInputParameterRuntimeBinding.GetRouteParameters(payload, qb.Params);

            // msg was the one that triggered it.
            RuntimeBindingInputs ctx = new NewQueueMessageRuntimeBindingInputs(func.Location, msg)
            {
                NameParameters = p
            };

            var instance = BindParameters(ctx, func);

            instance.TriggerReason = new QueueMessageTriggerReason
            {
                QueueName = qb.QueueName,
                MessageId = msg.Id,
                ParentGuid = GetOwnerFromMessage(msg)
            };

            return instance;
        }

        // policy: blobInput is the first [Input] attribute. Functions are triggered by single input.        
        public static FunctionInvokeRequest GetFunctionInvocation(FunctionDefinition func, ICloudBlob blobInput)
        {
            // blobInput was the one that triggered it.
            // Get the path from the first blob input parameter.
            var flow = func.Flow;
            CloudBlobPath firstInput = flow.Bindings.OfType<BlobParameterStaticBinding>().Where(b => b.IsInput).Select(b => b.Path).FirstOrDefault();

            var p = firstInput.Match(new CloudBlobPath(blobInput));
            if (p == null)
            {
                // No match.
                return null;
            }

            var ctx = new NewBlobRuntimeBindingInputs(func.Location, blobInput)
            {
                NameParameters = p,
            };
            FunctionInvokeRequest instance = BindParameters(ctx, func);

            Guid parentGuid = GetBlobWriterGuid(blobInput);
            instance.TriggerReason = new BlobTriggerReason
            {
                BlobPath = new CloudBlobPath(blobInput),
                ParentGuid = parentGuid
            };
            return instance;
        }

        // Bind the entire flow to an instance
        public static FunctionInvokeRequest BindParameters(RuntimeBindingInputs ctx, FunctionDefinition func)
        {
            FunctionFlow flow = func.Flow;
            int len = flow.Bindings.Length;

            var args = Array.ConvertAll(flow.Bindings, staticBinding => BindParameter(ctx, staticBinding));

            FunctionInvokeRequest instance = new FunctionInvokeRequest
            {
                Location = func.Location,
                Args = args
            };

            if (ctx.NameParameters != null && ctx.NameParameters.Count > 0)
            {
                instance.ParametersDisplayText = String.Join(", ", ctx.NameParameters.Values);
            }

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

            void ITriggerInvoke.OnNewQueueItem(CloudQueueMessage msg, QueueTrigger trigger, CancellationToken token)
            {
                FunctionDefinition func = (FunctionDefinition)trigger.Tag;

                if (func == null)
                {
                    _parent.InvokeFromDashboard(msg, token);
                }
                else
                {
                    _parent.OnNewQueueItem(msg, func, token);
                }
            }

            void ITriggerInvoke.OnNewBlob(ICloudBlob blob, BlobTrigger trigger, CancellationToken token)
            {
                FunctionDefinition func = (FunctionDefinition)trigger.Tag;
                _parent.OnNewBlob(func, blob, token);
            }
        }

        private class PartialFunctionLocation : FunctionLocation
        {
            public string Id { get; set; }

            public PartialFunctionLocation(string id)
            {
                Id = id;
                FullName = id;
            }

            public override string GetId()
            {
                return Id;
            }

            public override string GetShortName()
            {
                return Id;
            }
        }

        private class PartialParameterRuntimeBinding : ParameterRuntimeBinding
        {
            private readonly string _name;
            private readonly string _value;

            public PartialParameterRuntimeBinding(string name, string value)
            {
                _name = name;
                _value = value;
            }

            public override string ConvertToInvokeString()
            {
                return _value;
            }

            public override BindResult Bind(IConfiguration config, IBinderEx bindingContext, ParameterInfo targetParameter)
            {
                throw new InvalidOperationException("A PartialParameterRuntimeBinding cannot be bound.");
            }
        }
    }
}
