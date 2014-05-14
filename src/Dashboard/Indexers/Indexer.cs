using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dashboard.Data;
using Microsoft.Azure.Jobs;
using Microsoft.Azure.Jobs.Host.Bindings.StaticBindings;
using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.Indexers
{
    internal class Indexer : IIndexer
    {
        private readonly IPersistentQueue<PersistentQueueMessage> _queue;
        private readonly IFunctionTable _functionTable;
        private readonly IFunctionInstanceLogger _functionInstanceLogger;
        private readonly ICausalityLogger _causalityLogger;
        private readonly IFunctionsInJobIndexer _functionsInJobIndexer;

        public Indexer(IPersistentQueue<PersistentQueueMessage> queue, IFunctionTable functionTable,
            IFunctionInstanceLogger functionInstanceLogger, ICausalityLogger causalityLogger,
            IFunctionsInJobIndexer functionsInJobIndexer)
        {
            _queue = queue;
            _functionTable = functionTable;
            _functionInstanceLogger = functionInstanceLogger;
            _causalityLogger = causalityLogger;
            _functionsInJobIndexer = functionsInJobIndexer;
        }

        public void Update()
        {
            PersistentQueueMessage message = _queue.Dequeue();

            while (message != null)
            {
                Process(message);
                _queue.Delete(message);

                message = _queue.Dequeue();
            }
        }

        private void Process(PersistentQueueMessage message)
        {
            HostStartedMessage hostStartedMessage = message as HostStartedMessage;

            if (hostStartedMessage != null)
            {
                Process(hostStartedMessage);
                return;
            }

            FunctionStartedMessage functionStartedMessage = message as FunctionStartedMessage;

            if (functionStartedMessage != null)
            {
                Process(functionStartedMessage);
                return;
            }

            FunctionCompletedMessage functionCompletedMessage = message as FunctionCompletedMessage;

            if (functionCompletedMessage != null)
            {
                Process(functionCompletedMessage);
                return;
            }

            string errorMessage =
                String.Format(CultureInfo.InvariantCulture, "Unknown message type '{0}'.", message.Type);
            throw new InvalidOperationException(errorMessage);
        }

        private void Process(HostStartedMessage message)
        {
            Guid hostInstanceId = message.HostInstanceId;

            Guid hostId = message.HostId;

            IEnumerable<FunctionDefinition> existingFunctions = _functionTable.ReadAll().Where(f => f.HostId == hostId);

            DateTimeOffset hostInstanceCreatedOn = message.EnqueuedOn;

            IEnumerable<FunctionDefinition> functionsToDelete = existingFunctions.Where(f => !f.HostVersion.HasValue || f.HostVersion.Value < hostInstanceCreatedOn);

            foreach (FunctionDefinition deleteFunction in functionsToDelete)
            {
                _functionTable.Delete(deleteFunction);
            }

            DateTimeOffset mostRecentHostInstanceDate = existingFunctions.Where(f => f.HostVersion.HasValue).Select(f => f.HostVersion.Value).OrderByDescending(d => d).FirstOrDefault();

            if (hostInstanceCreatedOn > mostRecentHostInstanceDate)
            {
                List<FunctionDefinition> newFunctions = new List<FunctionDefinition>();

                foreach (FunctionDescriptor function in message.Functions)
                {
                    newFunctions.Add(ToFunctionDefinition(message, hostInstanceCreatedOn, function));
                }

                foreach (FunctionDefinition functionToAdd in newFunctions)
                {
                    functionToAdd.HostId = hostId;
                    functionToAdd.HostVersion = hostInstanceCreatedOn;
                    _functionTable.Add(functionToAdd);
                }
            }
        }

        private static FunctionDefinition ToFunctionDefinition(HostStartedMessage hostStartedMessage,
            DateTimeOffset hostInstanceCreatedOn, FunctionDescriptor functionDescriptor)
        {
            return new FunctionDefinition
            {
                HostId = hostStartedMessage.HostId,
                HostVersion = hostInstanceCreatedOn,
                Location = new DataOnlyFunctionLocation
                {
                    Id = functionDescriptor.Id,
                    ShortName = functionDescriptor.ShortName,
                    FullName = functionDescriptor.FullName,
                    AccountConnectionString = hostStartedMessage.StorageConnectionString,
                    ServiceBusConnectionString = hostStartedMessage.ServiceBusConnectionString
                },
                Flow = new FunctionFlow
                {
                    Bindings = ToStaticBindings(functionDescriptor.Parameters)
                }
            };
        }

        private static ParameterStaticBinding[] ToStaticBindings(IDictionary<string, ParameterDescriptor> parameters)
        {
            List<ParameterStaticBinding> bindings = new List<ParameterStaticBinding>();

            foreach (KeyValuePair<string, ParameterDescriptor> parameter in parameters)
            {
                bindings.Add(ToStaticBinding(parameter.Key, parameter.Value));
            }

            return bindings.ToArray();
        }

        private static ParameterStaticBinding ToStaticBinding(string name, ParameterDescriptor parameterDescriptor)
        {
            switch (parameterDescriptor.Type)
            {
                case "IBinder":
                    return new BinderParameterStaticBinding { Name = name };
                case "Blob":
                    BlobParameterDescriptor blobDescriptor = (BlobParameterDescriptor)parameterDescriptor;
                    return new BlobParameterStaticBinding
                    {
                        Name = name,
                        Path = new CloudBlobPath(blobDescriptor.ContainerName, blobDescriptor.BlobName),
                        IsInput = blobDescriptor.IsInput,
                    };
                case "CancellationToken":
                    return new CancellationTokenParameterStaticBinding { Name = name };
                case "CloudStorageAccount":
                    return new CloudStorageAccountParameterStaticBinding { Name = name };
                case "Invoke":
                    return new InvokeParameterStaticBinding { Name = name };
                case "Queue":
                    QueueParameterDescriptor queueDescriptor = (QueueParameterDescriptor)parameterDescriptor;
                    return new QueueParameterStaticBinding
                    {
                        Name = name,
                        QueueName = queueDescriptor.QueueName,
                        IsInput = queueDescriptor.IsInput,
                    };
                case "Route":
                    return new NameParameterStaticBinding { Name = name };
                case "ServiceBus":
                    ServiceBusParameterDescriptor serviceBusDescriptor = (ServiceBusParameterDescriptor)parameterDescriptor;
                    return new ServiceBusParameterStaticBinding
                    {
                        Name = name,
                        EntityPath = serviceBusDescriptor.EntityPath,
                        IsInput = serviceBusDescriptor.IsInput
                    };
                case "TableEntity":
                    TableParameterDescriptor tableDescriptor = (TableParameterDescriptor)parameterDescriptor;
                    return new TableParameterStaticBinding
                    {
                        Name = name,
                        TableName = tableDescriptor.TableName
                    };
                case "Table":
                    TableEntityParameterDescriptor tableEntityDescriptor = (TableEntityParameterDescriptor)parameterDescriptor;
                    return new TableEntityParameterStaticBinding
                    {
                        Name = name,
                        TableName = tableEntityDescriptor.TableName,
                        PartitionKey = tableEntityDescriptor.PartitionKey,
                        RowKey = tableEntityDescriptor.RowKey
                    };
                default:
                    throw new InvalidOperationException(String.Format(CultureInfo.InvariantCulture,
                        "Unknown parameter type '{0}'.", parameterDescriptor.Type));
            }
        }

        private void Process(FunctionStartedMessage message)
        {
            _functionInstanceLogger.LogFunctionStarted(message.Snapshot);
            _causalityLogger.LogTriggerReason(CreateTriggerReason(message.Snapshot));

            if (message.Snapshot.WebJobRunIdentifier != null)
            {
                _functionsInJobIndexer.RecordFunctionInvocationForJobRun(message.Snapshot.FunctionInstanceId, message.Snapshot.StartTime.UtcDateTime, message.Snapshot.WebJobRunIdentifier);
            }
        }

        internal static TriggerReason CreateTriggerReason(FunctionStartedSnapshot snapshot)
        {
            if (!snapshot.ParentId.HasValue && String.IsNullOrEmpty(snapshot.Reason))
            {
                return null;
            }

            return new InvokeTriggerReason
            {
                ChildGuid = snapshot.FunctionInstanceId,
                ParentGuid = snapshot.ParentId.GetValueOrDefault(),
                Message = snapshot.Reason
            };
        }

        private void Process(FunctionCompletedMessage message)
        {
            _functionInstanceLogger.LogFunctionCompleted(message.Snapshot);
        }

        private class DataOnlyFunctionLocation : FunctionLocation
        {
            public string Id { get; set; }

            public string ShortName { get; set; }

            public override string GetId()
            {
                return Id;
            }

            public override string GetShortName()
            {
                return ShortName;
            }
        }
    }
}