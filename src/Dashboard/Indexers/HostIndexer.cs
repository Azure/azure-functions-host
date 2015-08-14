// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Dashboard.Data;
using Microsoft.Azure.WebJobs.Protocols;

namespace Dashboard.Indexers
{
    internal class HostIndexer : IHostIndexer
    {
        private readonly IHostIndexManager _hostIndexManager;
        private readonly IFunctionIndexManager _functionIndexManager;
        private readonly IFunctionIndexVersionManager _functionIndexVersionManager;

        public HostIndexer(IHostIndexManager hostIndexManager, IFunctionIndexManager functionIndexManager,
            IFunctionIndexVersionManager functionIndexVersionManager)
        {
            _hostIndexManager = hostIndexManager;
            _functionIndexManager = functionIndexManager;
            _functionIndexVersionManager = functionIndexVersionManager;
        }

        // This method runs concurrently with other index processing.
        // Ensure all logic here is idempotent.
        [SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        public void ProcessHostStarted(HostStartedMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException("message");
            }

            string hostId = message.SharedQueueName;
            DateTimeOffset hostVersion = message.EnqueuedOn;
            DateTime hostVersionUtc = hostVersion.UtcDateTime;
            IEnumerable<FunctionDescriptor> messageFunctions = message.Functions ?? Enumerable.Empty<FunctionDescriptor>();

            HostSnapshot newSnapshot = new HostSnapshot
            {
                HostVersion = hostVersion,
                FunctionIds = CreateFunctionIds(messageFunctions)
            };

            if (_hostIndexManager.UpdateOrCreateIfLatest(hostId, newSnapshot))
            {
                IEnumerable<VersionedMetadata> existingFunctions = _functionIndexManager.List(hostId);

                IEnumerable<VersionedMetadata> removedFunctions = existingFunctions
                    .Where((f) => !newSnapshot.FunctionIds.Any(i => f.Id == i));

                foreach (VersionedMetadata removedFunction in removedFunctions)
                {
                    // Remove all functions no longer in our list (unless they exist with a later host version than
                    // ours).
                    string fullId = new FunctionIdentifier(hostId, removedFunction.Id).ToString();
                    _functionIndexManager.DeleteIfLatest(fullId, hostVersion, removedFunction.ETag,
                        removedFunction.Version);
                }

                HeartbeatDescriptor heartbeat = message.Heartbeat;

                IEnumerable<FunctionDescriptor> addedFunctions = messageFunctions
                    .Where((d) => !existingFunctions.Any(f => d.Id == f.Id));

                foreach (FunctionDescriptor addedFunction in addedFunctions)
                {
                    // Create any functions just appearing in our list (or update existing ones if they're earlier than
                    // ours).
                    FunctionSnapshot snapshot = CreateFunctionSnapshot(hostId, heartbeat, addedFunction, hostVersion);
                    _functionIndexManager.CreateOrUpdateIfLatest(snapshot);
                }

                // Update any functions appearing in both lists provided they're still earlier than ours (or create them
                // if they've since been deleted).
                var possiblyUpdatedFunctions = existingFunctions
                    .Join(messageFunctions, (f) => f.Id, (d) => d.Id, (f, d) => new
                    {
                        Descriptor = d,
                        HostVersion = f.Version,
                        ETag = f.ETag
                    });

                foreach (var possiblyUpdatedFunction in possiblyUpdatedFunctions)
                {
                    FunctionSnapshot snapshot = CreateFunctionSnapshot(hostId, heartbeat,
                        possiblyUpdatedFunction.Descriptor, hostVersion);
                    _functionIndexManager.UpdateOrCreateIfLatest(snapshot, possiblyUpdatedFunction.ETag,
                        possiblyUpdatedFunction.HostVersion);
                }
            }

            // Delete any functions we may have added or updated that are no longer in the index.

            // The create and update calls above may have occured after another instance started processing a later
            // version of this host. If that instance had already read the existing function list before we added the
            // function, it would think the function had already been deleted. In the end, we can't leave a function
            // around unless it's still in the host index after we've added or updated it.

            HostSnapshot finalSnapshot = _hostIndexManager.Read(hostId);
            IEnumerable<FunctionDescriptor> functionsRemovedAfterThisHostVersion;

            if (finalSnapshot == null)
            {
                functionsRemovedAfterThisHostVersion = messageFunctions;
            }
            else if (finalSnapshot.HostVersion.UtcDateTime > hostVersionUtc)
            {
                // Note that we base the list of functions to delete on what's in the HostStartedMessage, not what's in
                // the addedFunctions and possibleUpdatedFunctions variables, as this instance could have been aborted
                // and resumed and could lose state like those local variables.
                functionsRemovedAfterThisHostVersion = messageFunctions.Where(
                    f => !finalSnapshot.FunctionIds.Any((i) => f.Id == i));
            }
            else
            {
                functionsRemovedAfterThisHostVersion = Enumerable.Empty<FunctionDescriptor>();
            }

            foreach (FunctionDescriptor functionNoLongerInSnapshot in functionsRemovedAfterThisHostVersion)
            {
                string fullId = new FunctionIdentifier(hostId, functionNoLongerInSnapshot.Id).ToString();
                _functionIndexManager.DeleteIfLatest(fullId, hostVersionUtc);
            }

            _functionIndexVersionManager.UpdateOrCreateIfLatest(newSnapshot.HostVersion);
        }

        private static IEnumerable<string> CreateFunctionIds(IEnumerable<FunctionDescriptor> functions)
        {
            Debug.Assert(functions != null);
            List<string> ids = new List<string>();

            foreach (FunctionDescriptor function in functions)
            {
                ids.Add(function.Id);
            }

            return ids;
        }

        private static FunctionSnapshot CreateFunctionSnapshot(string queueName, HeartbeatDescriptor heartbeat,
            FunctionDescriptor function, DateTimeOffset hostVersion)
        {
            return new FunctionSnapshot
            {
                HostVersion = hostVersion,
                Id = new FunctionIdentifier(queueName, function.Id).ToString(),
                QueueName = queueName,
                HeartbeatSharedContainerName = heartbeat != null ? heartbeat.SharedContainerName : null,
                HeartbeatSharedDirectoryName = heartbeat != null ? heartbeat.SharedDirectoryName : null,
                HeartbeatExpirationInSeconds = heartbeat != null ? (int?)heartbeat.ExpirationInSeconds : null,
                HostFunctionId = function.Id,
                FullName = function.FullName,
                ShortName = function.ShortName,
                Parameters = CreateParameterSnapshots(function.Parameters)
            };
        }

        private static IDictionary<string, ParameterSnapshot> CreateParameterSnapshots(IEnumerable<ParameterDescriptor> parameters)
        {
            IDictionary<string, ParameterSnapshot> snapshots = new Dictionary<string, ParameterSnapshot>();

            if (parameters != null)
            {
                foreach (ParameterDescriptor parameter in parameters)
                {
                    ParameterSnapshot snapshot = CreateParameterSnapshot(parameter);

                    if (snapshot != null)
                    {
                        snapshots.Add(parameter.Name, snapshot);
                    }
                }
            }

            return snapshots;
        }

        [SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        internal static ParameterSnapshot CreateParameterSnapshot(ParameterDescriptor parameter)
        {
            // If display hints have already been provided by the descriptor
            // use them. Otherwise, we construct a new snapshot below. Note that
            // for extensibility (e.g. custom binding extensions), this is the
            // mechanism that must be used, since the Dashboard doesn't share type info
            // with custom extensions, we won't have access to the actual type as we do below.
            if (parameter.DisplayHints != null)
            {
                return new DisplayHintsParameterSnapshot(parameter.DisplayHints);
            }

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
                    // TEMP: This is here for back compat
                    // Latest versions of the SDK send a display info
                    // via ParameterDescriptor.DisplayHints
                    ServiceBusParameterDescriptor serviceBusParameter = (ServiceBusParameterDescriptor)parameter;
                    return new ServiceBusParameterSnapshot
                    {
                        EntityPath = serviceBusParameter.QueueOrTopicName,
                        IsInput = false
                    };
                case "ServiceBusTrigger":
                    // TEMP: This is here for back compat
                    // Latest versions of the SDK send display info
                    // via ParameterDescriptor.DisplayHints
                    ServiceBusTriggerParameterDescriptor serviceBusTriggerParameter = (ServiceBusTriggerParameterDescriptor)parameter;
                    return new ServiceBusParameterSnapshot
                    {
                        EntityPath = serviceBusTriggerParameter.QueueName != null ?
                            serviceBusTriggerParameter.QueueName :
                            serviceBusTriggerParameter.TopicName + "/Subscriptions/" + serviceBusTriggerParameter.SubscriptionName,
                        IsInput = true
                    };
                case "CallerSupplied":
                case "BindingData":
                    return new InvokeParameterSnapshot();
                default:
                    // Don't convert parameters that aren't used for invoke purposes.
                    return null;
            }
        }
    }
}
