using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Jobs;
using Microsoft.Azure.Jobs.Host.Storage.Table;
using Microsoft.Azure.Jobs.Protocols;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace Dashboard.Data
{
    // Primary access to the azure table storing function invoke requests.
    internal class FunctionInstanceLogger : IFunctionInstanceLogger, IFunctionQueuedLogger
    {
        private const string PartitionKey = "1";

        private readonly ICloudTable _table;

        public FunctionInstanceLogger(ICloudTableClient tableClient)
            : this(tableClient.GetTableReference(DashboardTableNames.FunctionInvokeLogTableName))
        {
        }

        public FunctionInstanceLogger(ICloudTable table)
        {
            if (table == null)
            {
                throw new ArgumentNullException("table");
            }
            _table = table;
        }

        // Using FunctionStartedSnapshot is a slight abuse; StartTime here is really QueueTime.
        public void LogFunctionQueued(FunctionStartedMessage message)
        {
            FunctionInstanceEntityGroup group = CreateEntityGroup(message);
            // Fix the abuse of FunctionStartedSnapshot.
            group.InstanceEntity.StartTime = null;
            _table.Insert(group.GetEntities());
        }

        public void LogFunctionStarted(FunctionStartedMessage message)
        {
            FunctionInstanceEntityGroup group = CreateEntityGroup(message);

            // Which operation to run depends on whether or not the entity currently exists in the "queued" status.
            FunctionInstanceEntityGroup existingGroup = FunctionInstanceEntityGroup.Lookup(_table, message.FunctionInstanceId);

            bool previouslyQueued;

            // If the existing entity doesn't contain a StartTime, it must be in the "queued" status.
            if (existingGroup != null && !existingGroup.InstanceEntity.StartTime.HasValue)
            {
                previouslyQueued = true;
            }
            else
            {
                previouslyQueued = false;
            }

            IEnumerable<ITableEntity> entities = group.GetEntities();

            if (!previouslyQueued)
            {
                LogFunctionStartedWhenNotPreviouslyQueued(entities);
            }
            else
            {
                CopyETags(existingGroup, group);
                LogFunctionStartedWhenPreviouslyQueued(entities);
            }
        }

        private static void CopyETags(FunctionInstanceEntityGroup source, FunctionInstanceEntityGroup destination)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }
            else if (destination == null)
            {
                throw new ArgumentNullException("destination");
            }
            else if (source.ArgumentEntities.Count != destination.ArgumentEntities.Count)
            {
                throw new InvalidOperationException("Count of arguments has changed.");
            }

            destination.InstanceEntity.ETag = source.InstanceEntity.ETag;

            for (int index = 0; index < source.ArgumentEntities.Count; index++)
            {
                destination.ArgumentEntities[index].ETag = source.ArgumentEntities[index].ETag;
            }
        }

        private void LogFunctionStartedWhenNotPreviouslyQueued(IEnumerable<ITableEntity> entities)
        {
            try
            {
                // LogFunctionStarted and LogFunctionCompleted may run concurrently. Ensure LogFunctionStarted loses by
                // just doing a simple Insert that will fail if the entity already exists. LogFunctionCompleted wins by
                // having it replace the LogFunctionStarted record, if any.
                _table.Insert(entities);
            }
            catch (StorageException exception)
            {
                // If there's a conflict, LogFunctionCompleted already ran, so there's no work to do here.

                if (!exception.IsConflict())
                {
                    throw;
                }
            }
        }

        private void LogFunctionStartedWhenPreviouslyQueued(IEnumerable<ITableEntity> entities)
        {
            try
            {
                // LogFunctionStarted and LogFunctionCompleted may run concurrently. LogFunctionQueued does not run
                // concurrently. Ensure LogFunctionStarted wins over LogFunctionQueued but loses to LogFunctionCompleted
                // by doing a Replace with ETag check that will fail if the entity has been changed.
                // LogFunctionCompleted wins by doing a Replace without an ETag check, so it will replace the
                // LogFunctionStarted (or Queued) record, if any.
                _table.Replace(entities);
            }
            catch (StorageException exception)
            {
                // If the ETag doesn't match, LogFunctionCompleted already ran, so there's no work to do here.

                if (!exception.IsPreconditionFailed())
                {
                    throw;
                }
            }
        }

        public void LogFunctionCompleted(FunctionCompletedMessage message)
        {
            FunctionInstanceEntityGroup group = CreateEntityGroup(message);

            // LogFunctionStarted and LogFunctionCompleted may run concurrently. Ensure LogFunctionCompleted wins by
            // having it replace the LogFunctionStarted record, if any.
            _table.InsertOrReplace(group.GetEntities());
        }

        private static FunctionInstanceEntityGroup CreateEntityGroup(FunctionStartedMessage message)
        {
            FunctionInstanceEntity instanceEntity = CreateInstanceEntity(message);
            IReadOnlyList<FunctionArgumentEntity> argumentEntities = CreateArgumentEntities(message.FunctionInstanceId,
                message.Function, message.Arguments);

            return new FunctionInstanceEntityGroup
            {
                InstanceEntity = instanceEntity,
                ArgumentEntities = argumentEntities
            };
        }

        private static FunctionInstanceEntity CreateInstanceEntity(FunctionStartedMessage message)
        {
            return new FunctionInstanceEntity
            {
                PartitionKey = PartitionKey,
                RowKey = FunctionInstanceEntity.GetRowKey(message.FunctionInstanceId),
                Id = message.FunctionInstanceId,
                HostId = message.HostId,
                HostInstanceId = message.HostInstanceId,
                FunctionId = new FunctionIdentifier(message.HostId, message.Function.Id).ToString(),
                FunctionFullName = message.Function.FullName,
                FunctionShortName = message.Function.ShortName,
                ParentId = message.ParentId,
                Reason = message.Reason,
                QueueTime = message.StartTime,
                StartTime = message.StartTime,
                StorageConnectionString = message.StorageConnectionString,
                OutputBlobUrl = message.OutputBlobUrl,
                ParameterLogBlobUrl = message.ParameterLogBlobUrl,
                WebSiteName = message.WebJobRunIdentifier != null ? message.WebJobRunIdentifier.WebSiteName : null,
                WebJobType = message.WebJobRunIdentifier != null ? message.WebJobRunIdentifier.JobType.ToString() : null,
                WebJobName = message.WebJobRunIdentifier != null ? message.WebJobRunIdentifier.JobName : null,
                WebJobRunId = message.WebJobRunIdentifier != null ? message.WebJobRunIdentifier.RunId : null
            };
        }

        private static IReadOnlyList<FunctionArgumentEntity> CreateArgumentEntities(Guid functionInstanceId,
            FunctionDescriptor function, IDictionary<string, string> arguments)
        {
            List<FunctionArgumentEntity> entities = new List<FunctionArgumentEntity>();
            int index = 0;

            foreach (KeyValuePair<string, string> argument in arguments)
            {
                entities.Add(new FunctionArgumentEntity
                {
                    PartitionKey = PartitionKey,
                    RowKey = FunctionArgumentEntity.GetRowKey(functionInstanceId, index),
                    Name = argument.Key,
                    Value = argument.Value,
                    IsBlob = function.Parameters != null && function.Parameters.ContainsKey(argument.Key)
                        && function.Parameters[argument.Key] is BlobParameterDescriptor,
                });

                index++;
            }

            return entities;
        }

        private static FunctionInstanceEntityGroup CreateEntityGroup(FunctionCompletedMessage message)
        {
            FunctionInstanceEntity instanceEntity = CreateInstanceEntity(message);
            IReadOnlyList<FunctionArgumentEntity> argumentEntities = CreateArgumentEntities(message.FunctionInstanceId,
                message.Function, message.Arguments);

            return new FunctionInstanceEntityGroup
            {
                InstanceEntity = instanceEntity,
                ArgumentEntities = argumentEntities
            };
        }

        private static FunctionInstanceEntity CreateInstanceEntity(FunctionCompletedMessage message)
        {
            FunctionInstanceEntity entity = CreateInstanceEntity((FunctionStartedMessage)message);
            entity.EndTime = message.EndTime;
            entity.Succeeded = message.Succeeded;
            entity.ExceptionType = message.ExceptionType;
            entity.ExceptionMessage = message.ExceptionMessage;
            return entity;
        }
    }
}
