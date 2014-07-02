using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dashboard.HostMessaging;
using Microsoft.Azure.Jobs.Protocols;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    internal class FunctionInstanceLogger : IFunctionInstanceLogger, IFunctionQueuedLogger
    {
        private readonly IConcurrentDocumentStore<FunctionInstanceSnapshot> _store;

        public FunctionInstanceLogger(CloudBlobClient client)
            : this(ConcurrentDocumentStore.CreateJsonBlobStore<FunctionInstanceSnapshot>(
                client, DashboardContainerNames.Dashboard, DashboardDirectoryNames.FunctionInstances))
        {
        }

        private FunctionInstanceLogger(IConcurrentDocumentStore<FunctionInstanceSnapshot> store)
        {
            _store = store;
        }

        public void LogFunctionQueued(Guid id, IDictionary<string, string> arguments, Guid? parentId,
            DateTimeOffset queueTime, string functionId, string functionFullName, string functionShortName)
        {
            FunctionInstanceSnapshot snapshot = new FunctionInstanceSnapshot
            {
                Id = id,
                FunctionId = functionId,
                FunctionFullName = functionFullName,
                FunctionShortName = functionShortName,
                Arguments = CreateArguments(arguments),
                ParentId = parentId,
                Reason = parentId.HasValue ? "Replayed from Dashboard." : "Ran from Dashboard.",
                QueueTime = queueTime
            };

            // Ignore the return result. Only the dashboard calls this method, and it does so before enqueuing the
            // message to the host to run the function. So realistically the blob can't already exist. And even if the
            // host did see the message before this call, an existing blob just means something more recent than
            // "queued" status, so there's nothing to do here in that case anyway.
            _store.TryCreate(GetId(id), snapshot);
        }

        private static IDictionary<string, FunctionInstanceArgument> CreateArguments(IDictionary<string, string> values)
        {
            Dictionary<string, FunctionInstanceArgument> arguments = new Dictionary<string, FunctionInstanceArgument>();

            foreach (KeyValuePair<string, string> value in values)
            {
                arguments.Add(value.Key, new FunctionInstanceArgument { Value = value.Value });
            }

            return arguments;
        }

        public void LogFunctionStarted(FunctionStartedMessage message)
        {
            FunctionInstanceSnapshot snapshot = CreateSnapshot(message);

            // Which operation to run depends on whether or not the entity currently exists in the "queued" status.
            IConcurrentDocument<FunctionInstanceSnapshot> existingSnapshot = _store.Read(GetId(message));

            bool previouslyQueued;

            // If the existing entity doesn't contain a StartTime, it must be in the "queued" status.
            if (existingSnapshot != null && existingSnapshot.Document != null && !existingSnapshot.Document.StartTime.HasValue)
            {
                previouslyQueued = true;
            }
            else
            {
                previouslyQueued = false;
            }

            if (!previouslyQueued)
            {
                LogFunctionStartedWhenNotPreviouslyQueued(snapshot);
            }
            else
            {
                LogFunctionStartedWhenPreviouslyQueued(snapshot, existingSnapshot.ETag);
            }
        }

        private void LogFunctionStartedWhenNotPreviouslyQueued(FunctionInstanceSnapshot snapshot)
        {
            // LogFunctionStarted and LogFunctionCompleted may run concurrently. Ensure LogFunctionStarted loses by just
            // doing a simple Insert that will fail if the entity already exists. LogFunctionCompleted wins by having it
            // replace the LogFunctionStarted record, if any.
            // Ignore the return value: if the item already exists, LogFunctionCompleted already ran, so there's no work
            // to do here.
            _store.TryCreate(GetId(snapshot), snapshot);
        }

        private void LogFunctionStartedWhenPreviouslyQueued(FunctionInstanceSnapshot snapshot, string etag)
        {
            // LogFunctionStarted and LogFunctionCompleted may run concurrently. LogFunctionQueued does not run
            // concurrently. Ensure LogFunctionStarted wins over LogFunctionQueued but loses to LogFunctionCompleted by
            // doing a Replace with ETag check that will fail if the entity has been changed.
            // LogFunctionCompleted wins by doing a Replace without an ETag check, so it will replace the
            // LogFunctionStarted (or Queued) record, if any.
            // Ignore the return value: if the ETag doesn't match, LogFunctionCompleted already ran, so there's no work
            // to do here.
            _store.TryUpdate(GetId(snapshot), etag, snapshot);
        }

        public void LogFunctionCompleted(FunctionCompletedMessage message)
        {
            FunctionInstanceSnapshot snapshot = CreateSnapshot(message);
            
            // The completed message includes the full parameter logs; delete the extra blob used for running status
            // updates.
            DeleteParameterLogBlob(message.Credentials, snapshot.ParameterLogBlob);
            snapshot.ParameterLogBlob = null;

            // LogFunctionStarted and LogFunctionCompleted may run concurrently. Ensure LogFunctionCompleted wins by
            // having it replace the LogFunctionStarted record, if any.
            _store.CreateOrUpdate(GetId(snapshot), snapshot);
        }

        private void DeleteParameterLogBlob(CredentialsDescriptor credentials, LocalBlobDescriptor blobDescriptor)
        {
            if (credentials == null || blobDescriptor == null)
            {
                return;
            }

            string connectionString = credentials.GetStorageConnectionString();

            if (connectionString == null)
            {
                return;
            }

            CloudBlockBlob blob = blobDescriptor.GetBlockBlob(connectionString);
            blob.DeleteIfExists();
        }

        private static FunctionInstanceSnapshot CreateSnapshot(FunctionStartedMessage message)
        {
            string storageConnectionString = message.Credentials != null ?
                message.Credentials.GetStorageConnectionString() : null;

            return new FunctionInstanceSnapshot
            {
                Id = message.FunctionInstanceId,
                HostInstanceId = message.HostInstanceId,
                InstanceQueueName = message.InstanceQueueName,
                Heartbeat = message.Heartbeat,
                FunctionId = new FunctionIdentifier(message.SharedQueueName, message.Function.Id).ToString(),
                FunctionFullName = message.Function.FullName,
                FunctionShortName = message.Function.ShortName,
                Arguments = CreateArguments(message.Function.Parameters, message.Arguments),
                ParentId = message.ParentId,
                Reason = message.FormatReason(),
                QueueTime = message.StartTime,
                StartTime = message.StartTime,
                StorageConnectionString = storageConnectionString,
                OutputBlob = message.OutputBlob,
                ParameterLogBlob = message.ParameterLogBlob,
                WebSiteName = message.WebJobRunIdentifier != null ? message.WebJobRunIdentifier.WebSiteName : null,
                WebJobType = message.WebJobRunIdentifier != null ? message.WebJobRunIdentifier.JobType.ToString() : null,
                WebJobName = message.WebJobRunIdentifier != null ? message.WebJobRunIdentifier.JobName : null,
                WebJobRunId = message.WebJobRunIdentifier != null ? message.WebJobRunIdentifier.RunId : null
            };
        }

        private static IDictionary<string, FunctionInstanceArgument> CreateArguments(IEnumerable<ParameterDescriptor> parameters,
            IDictionary<string, string> argumentValues)
        {
            IDictionary<string, FunctionInstanceArgument> arguments =
                new Dictionary<string, FunctionInstanceArgument>();

            foreach (KeyValuePair<string, string> item in argumentValues)
            {
                string name = item.Key;
                ParameterDescriptor descriptor = GetParameterDescriptor(parameters, name);
                BlobParameterDescriptor blobDescriptor = descriptor as BlobParameterDescriptor;
                arguments.Add(name, new FunctionInstanceArgument
                {
                    Value = item.Value,
                    IsBlob = blobDescriptor != null || descriptor is BlobTriggerParameterDescriptor,
                    IsBlobOutput = blobDescriptor != null && blobDescriptor.Access == FileAccess.Write
                });
            }

            return arguments;
        }

        private static ParameterDescriptor GetParameterDescriptor(IEnumerable<ParameterDescriptor> parameters, string name)
        {
            return parameters != null ? parameters.FirstOrDefault(p => p != null && p.Name == name) : null;
        }

        private static FunctionInstanceSnapshot CreateSnapshot(FunctionCompletedMessage message)
        {
            FunctionInstanceSnapshot entity = CreateSnapshot((FunctionStartedMessage)message);
            entity.ParameterLogs = message.ParameterLogs;
            entity.EndTime = message.EndTime;
            entity.Succeeded = message.Succeeded;
            entity.ExceptionType = message.Failure != null ? message.Failure.ExceptionType : null;
            entity.ExceptionMessage = message.Failure != null ? message.Failure.ExceptionDetails : null;
            return entity;
        }

        private static string GetId(FunctionInstanceSnapshot snapshot)
        {
            return GetId(snapshot.Id);
        }

        private static string GetId(FunctionStartedMessage message)
        {
            return GetId(message.FunctionInstanceId);
        }

        private static string GetId(Guid guid)
        {
            return guid.ToString("N");
        }
    }
}
