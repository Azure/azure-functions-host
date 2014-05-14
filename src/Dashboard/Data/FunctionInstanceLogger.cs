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
        public void LogFunctionQueued(FunctionStartedSnapshot snapshot)
        {
            FunctionInstanceEntityGroup group = CreateEntityGroup(snapshot);
            // Fix the abuse of FunctionStartedSnapshot.
            group.InstanceEntity.StartTime = null;
            _table.Insert(group.GetEntities());
        }

        public void LogFunctionStarted(FunctionStartedSnapshot snapshot)
        {
            FunctionInstanceEntityGroup group = CreateEntityGroup(snapshot);

            // Which operation to run depends on whether or not the entity currently exists in the "queued" status.
            FunctionInstanceEntityGroup existingGroup = FunctionInstanceEntityGroup.Lookup(_table, snapshot.FunctionInstanceId);

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
                RequestResult result = exception.RequestInformation;

                // If there's a conflict, LogFunctionCompleted already ran, so there's no work to do here.

                if (result.HttpStatusCode != 409)
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
                RequestResult result = exception.RequestInformation;

                // If the ETag doesn't match, LogFunctionCompleted already ran, so there's no work to do here.

                if (result.HttpStatusCode != 409)
                {
                    throw;
                }
            }
        }

        public void LogFunctionCompleted(FunctionCompletedSnapshot snapshot)
        {
            FunctionInstanceEntityGroup group = CreateEntityGroup(snapshot);

            // LogFunctionStarted and LogFunctionCompleted may run concurrently. Ensure LogFunctionCompleted wins by
            // having it replace the LogFunctionStarted record, if any.
            _table.InsertOrReplace(group.GetEntities());
        }

        private static FunctionInstanceEntityGroup CreateEntityGroup(FunctionStartedSnapshot snapshot)
        {
            FunctionInstanceEntity instanceEntity = CreateInstanceEntity(snapshot);
            IReadOnlyList<FunctionArgumentEntity> argumentEntities = CreateArgumentEntities(snapshot.FunctionInstanceId, snapshot.Arguments);

            return new FunctionInstanceEntityGroup
            {
                InstanceEntity = instanceEntity,
                ArgumentEntities = argumentEntities
            };
        }

        private static FunctionInstanceEntity CreateInstanceEntity(FunctionStartedSnapshot snapshot)
        {
            return new FunctionInstanceEntity
            {
                PartitionKey = PartitionKey,
                RowKey = FunctionInstanceEntity.GetRowKey(snapshot.FunctionInstanceId),
                Id = snapshot.FunctionInstanceId,
                HostInstanceId = snapshot.HostInstanceId,
                FunctionId = snapshot.FunctionId,
                FunctionFullName = snapshot.FunctionFullName,
                FunctionShortName = snapshot.FunctionShortName,
                ParentId = snapshot.ParentId,
                Reason = snapshot.Reason,
                QueueTime = snapshot.StartTime,
                StartTime = snapshot.StartTime,
                StorageConnectionString = snapshot.StorageConnectionString,
                OutputBlobUrl = snapshot.OutputBlobUrl,
                ParameterLogBlobUrl = snapshot.ParameterLogBlobUrl,
                WebSiteName = snapshot.WebJobRunIdentifier != null ? snapshot.WebJobRunIdentifier.WebSiteName : null,
                WebJobType = snapshot.WebJobRunIdentifier != null ? snapshot.WebJobRunIdentifier.JobType.ToString() : null,
                WebJobName = snapshot.WebJobRunIdentifier != null ? snapshot.WebJobRunIdentifier.JobName : null,
                WebJobRunId = snapshot.WebJobRunIdentifier != null ? snapshot.WebJobRunIdentifier.RunId : null
            };
        }

        private static IReadOnlyList<FunctionArgumentEntity> CreateArgumentEntities(Guid functionInstanceId,
            IDictionary<string, FunctionArgument> arguments)
        {
            List<FunctionArgumentEntity> entities = new List<FunctionArgumentEntity>();
            int index = 0;

            foreach (KeyValuePair<string, FunctionArgument> argument in arguments)
            {
                entities.Add(new FunctionArgumentEntity
                {
                    PartitionKey = PartitionKey,
                    RowKey = FunctionArgumentEntity.GetRowKey(functionInstanceId, index),
                    Name = argument.Key,
                    Value = argument.Value.Value,
                    IsBlob = argument.Value.IsBlob,
                    IsBlobInput = argument.Value.IsBlobInput
                });

                index++;
            }

            return entities;
        }

        private static FunctionInstanceEntityGroup CreateEntityGroup(FunctionCompletedSnapshot snapshot)
        {
            FunctionInstanceEntity instanceEntity = CreateInstanceEntity(snapshot);
            IReadOnlyList<FunctionArgumentEntity> argumentEntities = CreateArgumentEntities(snapshot.FunctionInstanceId, snapshot.Arguments);

            return new FunctionInstanceEntityGroup
            {
                InstanceEntity = instanceEntity,
                ArgumentEntities = argumentEntities
            };
        }

        private static FunctionInstanceEntity CreateInstanceEntity(FunctionCompletedSnapshot snapshot)
        {
            FunctionInstanceEntity entity = CreateInstanceEntity((FunctionStartedSnapshot)snapshot);
            entity.EndTime = snapshot.EndTime;
            entity.Succeeded = snapshot.Succeeded;
            entity.ExceptionType = snapshot.ExceptionType;
            entity.ExceptionMessage = snapshot.ExceptionMessage;
            return entity;
        }
    }
}
