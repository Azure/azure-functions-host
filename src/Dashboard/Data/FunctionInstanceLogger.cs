using System;
using System.Collections.Generic;
using AzureTables;
using Dashboard.ViewModels;
using Microsoft.Azure.Jobs;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace Dashboard.Data
{
    // Primary access to the azure table storing function invoke requests.
    internal class FunctionInstanceLogger : IFunctionInstanceLogger, IFunctionQueuedLogger
    {
        private const string PartionKey = "1";

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

        public void LogFunctionQueued(ExecutionInstanceLogEntity logEntity)
        {
            ITableEntity entity = CreateTableEntity(logEntity);
            _table.Insert(entity);
        }

        public void LogFunctionStarted(FunctionStartedSnapshot snapshot)
        {
            ExecutionInstanceLogEntity logEntity = CreateLogEntity(snapshot);
            ITableEntity entity = CreateTableEntity(logEntity);

            // Which operation to run depends on whether or not the entity currently exists in the "queued" status.
            DynamicTableEntity existingEntity = _table.Retrieve<DynamicTableEntity>(PartionKey, logEntity.GetKey());

            DynamicTableEntity queuedEntity;

            // If the existing entity doesn't contain a StartTime, it must be in the "queued" status.
            if (existingEntity != null && !existingEntity.Properties.ContainsKey("StartTime"))
            {
                queuedEntity = existingEntity;
            }
            else
            {
                existingEntity = null;
            }

            if (existingEntity == null)
            {
                LogFunctionStartedWhenNotPreviouslyQueued(entity);
            }
            else
            {
                LogFunctionStartedWhenPreviouslyQueued(entity, existingEntity.ETag);
            }
        }

        private static ExecutionInstanceLogEntity CreateLogEntity(FunctionStartedSnapshot snapshot)
        {
            return new ExecutionInstanceLogEntity
            {
                HostInstanceId = snapshot.HostInstanceId,
                ExecutingJobRunId = snapshot.WebJobRunIdentifier,
                FunctionInstance = CreateFunctionInstance(snapshot),
                StartTime = snapshot.StartTime.UtcDateTime,
                OutputUrl = snapshot.OutputBlobUrl,
                ParameterLogUrl = snapshot.ParameterLogBlobUrl
            };
        }

        private static FunctionInvokeRequest CreateFunctionInstance(FunctionStartedSnapshot snapshot)
        {
            FunctionInvokeRequest request = new FunctionInvokeRequest
            {
                Id = snapshot.FunctionInstanceId,
                TriggerReason = Dashboard.Indexers.Indexer.CreateTriggerReason(snapshot),
                Location = CreateLocation(snapshot),
                Args = CreateArgs(snapshot.Arguments)
            };
            request.ParametersDisplayText = InvocationLogViewModel.BuildFunctionDisplayTitle(request);
            return request;
        }

        private static FunctionLocation CreateLocation(FunctionStartedSnapshot snapshot)
        {
            return new DataOnlyFunctionLocation(snapshot.FunctionId, snapshot.FunctionShortName,
                snapshot.FunctionFullName, snapshot.StorageConnectionString, snapshot.ServiceBusConnectionString);
        }

        private static ParameterRuntimeBinding[] CreateArgs(IDictionary<string, FunctionArgument> arguments)
        {
            ParameterRuntimeBinding[] args = new ParameterRuntimeBinding[arguments.Count];
            int index = 0;

            foreach (KeyValuePair<string, FunctionArgument> argument in arguments)
            {
                FunctionArgument argumentValue = argument.Value;
                args[index] = new DataOnlyParameterRuntimeBinding(argument.Key, argumentValue.Value,
                    argumentValue.IsBlob, argumentValue.IsBlobInput);
                index++;
            }

            return args;
        }

        private void LogFunctionStartedWhenNotPreviouslyQueued(ITableEntity entity)
        {
            try
            {
                // LogFunctionStarted and LogFunctionCompleted may run concurrently. Ensure LogFunctionStarted loses by
                // just doing a simple Insert that will fail if the entity already exists. LogFunctionCompleted wins by
                // having it replace the LogFunctionStarted record, if any.
                _table.Insert(entity);
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

        private void LogFunctionStartedWhenPreviouslyQueued(ITableEntity entity, string etag)
        {
            entity.ETag = etag;

            try
            {
                // LogFunctionStarted and LogFunctionCompleted may run concurrently. LogFunctionQueued does not run
                // concurrently. Ensure LogFunctionStarted wins over LogFunctionQueued but loses to LogFunctionCompleted
                // by doing a Replace with ETag check that will fail if the entity has been changed.
                // LogFunctionCompleted wins by doing a Replace without an ETag check, so it will replace the
                // LogFunctionStarted (or Queued) record, if any.
                _table.Replace(entity);
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

        public void LogFunctionCompleted(ExecutionInstanceLogEntity logEntity)
        {
            ITableEntity entity = CreateTableEntity(logEntity);

            // LogFunctionStarted and LogFunctionCompleted may run concurrently. Ensure LogFunctionCompleted wins by
            // having it replace the LogFunctionStarted record, if any.
            _table.InsertOrReplace(entity);
        }

        private static ITableEntity CreateTableEntity(ExecutionInstanceLogEntity logEntity)
        {
            return AzureTable.ToTableEntity(PartionKey, logEntity.GetKey(), logEntity);
        }

        private class DataOnlyFunctionLocation : FunctionLocation
        {
            public string Id { get; set; }
            public string ShortName { get; set; }

            public DataOnlyFunctionLocation(string id, string shortName, string fullName,
                string storageConnectionString, string serviceBusConnectionString)
            {
                Id = id;
                ShortName = shortName;
                FullName = fullName;
                AccountConnectionString = storageConnectionString;
                ServiceBusConnectionString = serviceBusConnectionString;
            }

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
