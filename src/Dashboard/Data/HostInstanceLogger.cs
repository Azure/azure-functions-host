using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Azure.Jobs.Protocols;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    internal class HostInstanceLogger : IHostInstanceLogger
    {
        private readonly IVersionedDocumentStore<HostSnapshot> _store;

        public HostInstanceLogger(CloudBlobClient blobClient)
            : this(blobClient.GetContainerReference(DashboardContainerNames.HostContainer))
        {
        }

        private HostInstanceLogger(CloudBlobContainer container)
        {
            _store = new VersionedDocumentStore<HostSnapshot>(container);
        }

        public void LogHostStarted(HostStartedMessage message)
        {
            string hostId = message.HostId.ToString();
            HostSnapshot newSnapshot = CreateSnapshot(message);

            VersionedDocument<HostSnapshot> existingVersionedSnapshot = _store.Read(hostId);

            if (existingVersionedSnapshot == null)
            {
                if (_store.TryCreate(message.HostId.ToString(), newSnapshot))
                {
                    return;
                }
            }

            while (existingVersionedSnapshot.Document.HostVersion < message.EnqueuedOn)
            {
                VersionedDocument<HostSnapshot> newVersionedSnapshot = new VersionedDocument<HostSnapshot>(newSnapshot,
                    existingVersionedSnapshot.ETag);

                if (_store.TryUpdate(hostId, newVersionedSnapshot))
                {
                    return;
                }

                existingVersionedSnapshot = _store.Read(hostId);
            }
        }

        private static HostSnapshot CreateSnapshot(HostStartedMessage message)
        {
            return new HostSnapshot
            {
                HostVersion = message.EnqueuedOn,
                Functions = CreateFunctionSnapshots(message.HostId, message.Functions)
            };
        }

        private static IEnumerable<FunctionSnapshot> CreateFunctionSnapshots(Guid hostId, IEnumerable<FunctionDescriptor> functions)
        {
            List<FunctionSnapshot> snapshots = new List<FunctionSnapshot>();

            foreach (FunctionDescriptor function in functions)
            {
                snapshots.Add(CreateFunctionSnapshot(hostId, function));
            }

            return snapshots;
        }

        private static FunctionSnapshot CreateFunctionSnapshot(Guid hostId, FunctionDescriptor function)
        {
            return new FunctionSnapshot
            {
                Id = String.Format(CultureInfo.InvariantCulture, "{0}_{1}", hostId, function.Id),
                HostId = hostId,
                HostFunctionId = function.Id,
                FullName = function.FullName,
                ShortName = function.ShortName,
                Parameters = CreateParameterSnapshots(function.Parameters)
            };
        }

        private static IDictionary<string, ParameterSnapshot> CreateParameterSnapshots(IDictionary<string, ParameterDescriptor> parameters)
        {
            IDictionary<string, ParameterSnapshot> snapshots = new Dictionary<string, ParameterSnapshot>();

            foreach (KeyValuePair<string, ParameterDescriptor> parameter in parameters)
            {
                ParameterSnapshot snapshot = CreateParameterSnapshot(parameter.Value);

                if (snapshot != null)
                {
                    snapshots.Add(parameter.Key, snapshot);
                }
            }

            return snapshots;
        }

        private static ParameterSnapshot CreateParameterSnapshot(ParameterDescriptor parameter)
        {
            switch (parameter.Type)
            {
                case "Blob":
                    BlobParameterDescriptor blobParameter = (BlobParameterDescriptor)parameter;
                    return new BlobParameterSnapshot
                    {
                        ContainerName = blobParameter.ContainerName,
                        BlobName = blobParameter.BlobName,
                        IsInput = blobParameter.Access == FileAccess.Read
                    };
                case "BlobTrigger":
                    BlobTriggerParameterDescriptor blobTriggerParameter = (BlobTriggerParameterDescriptor)parameter;
                    return new BlobParameterSnapshot
                    {
                        ContainerName = blobTriggerParameter.ContainerName,
                        BlobName = blobTriggerParameter.BlobName,
                        IsInput = true
                    };
                case "Queue":
                    QueueParameterDescriptor queueParameter = (QueueParameterDescriptor)parameter;
                    return new QueueParameterSnapshot
                    {
                        QueueName = queueParameter.QueueName,
                        IsInput = queueParameter.Access == FileAccess.Read
                    };
                case "QueueTrigger":
                    QueueTriggerParameterDescriptor queueTriggerParameter = (QueueTriggerParameterDescriptor)parameter;
                    return new QueueParameterSnapshot
                    {
                        QueueName = queueTriggerParameter.QueueName,
                        IsInput = true
                    };
                case "Table":
                    TableParameterDescriptor tableParameter = (TableParameterDescriptor)parameter;
                    return new TableParameterSnapshot
                    {
                        TableName = tableParameter.TableName
                    };
                case "TableEntity":
                    TableEntityParameterDescriptor tableEntityParameter = (TableEntityParameterDescriptor)parameter;
                    return new TableEntityParameterSnapshot
                    {
                        TableName = tableEntityParameter.TableName,
                        PartitionKey = tableEntityParameter.PartitionKey,
                        RowKey = tableEntityParameter.RowKey
                    };
                case "ServiceBus":
                    ServiceBusParameterDescriptor serviceBusParameter = (ServiceBusParameterDescriptor)parameter;
                    return new ServiceBusParameterSnapshot
                    {
                        EntityPath = serviceBusParameter.QueueOrTopicName,
                        IsInput = false
                    };
                case "ServiceBusTrigger":
                    ServiceBusTriggerParameterDescriptor serviceBusTriggerParameter = (ServiceBusTriggerParameterDescriptor)parameter;
                    return new ServiceBusParameterSnapshot
                    {
                        EntityPath = serviceBusTriggerParameter.QueueName != null ?
                            serviceBusTriggerParameter.QueueName :
                            serviceBusTriggerParameter.TopicName + "/Subscriptions/" + serviceBusTriggerParameter.SubscriptionName,
                        IsInput = true
                    };
                case "Invoke":
                case "Route":
                    return new InvokeParameterSnapshot();
                default:
                    // Don't convert parameters that aren't used for invoke purposes.
                    return null;
            }
        }
    }
}
