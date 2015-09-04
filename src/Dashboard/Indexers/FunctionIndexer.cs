// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dashboard.Data;
using Dashboard.HostMessaging;
using Microsoft.Azure.WebJobs.Protocols;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Indexers
{
    internal class FunctionIndexer : IFunctionIndexer
    {
        private readonly CloudStorageAccount _cloudStorageAccount;
        private readonly IFunctionInstanceLogger _functionInstanceLogger;
        private readonly IFunctionInstanceLookup _functionInstanceLookup;
        private readonly IFunctionStatisticsWriter _statisticsWriter;
        private readonly IRecentInvocationIndexWriter _recentInvocationsWriter;
        private readonly IRecentInvocationIndexByFunctionWriter _recentInvocationsByFunctionWriter;
        private readonly IRecentInvocationIndexByJobRunWriter _recentInvocationsByJobRunWriter;
        private readonly IRecentInvocationIndexByParentWriter _recentInvocationsByParentWriter;

        public FunctionIndexer(
            CloudStorageAccount cloudStorageAccount,
            IFunctionInstanceLogger functionInstanceLogger,
            IFunctionInstanceLookup functionInstanceLookup,
            IFunctionStatisticsWriter statisticsWriter,
            IRecentInvocationIndexWriter recentInvocationsWriter,
            IRecentInvocationIndexByFunctionWriter recentInvocationsByFunctionWriter,
            IRecentInvocationIndexByJobRunWriter recentInvocationsByJobRunWriter,
            IRecentInvocationIndexByParentWriter recentInvocationsByParentWriter)
        {
            _cloudStorageAccount = cloudStorageAccount;
            _functionInstanceLogger = functionInstanceLogger;
            _functionInstanceLookup = functionInstanceLookup;
            _statisticsWriter = statisticsWriter;
            _recentInvocationsWriter = recentInvocationsWriter;
            _recentInvocationsByFunctionWriter = recentInvocationsByFunctionWriter;
            _recentInvocationsByJobRunWriter = recentInvocationsByJobRunWriter;
            _recentInvocationsByParentWriter = recentInvocationsByParentWriter;
        }

        // This method runs concurrently with other index processing.
        // Ensure all logic here is idempotent.
        public void ProcessFunctionStarted(FunctionStartedMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException("message");
            }

            FunctionInstanceSnapshot snapshot = CreateSnapshot(message);
            _functionInstanceLogger.LogFunctionStarted(snapshot);

            string functionId = new FunctionIdentifier(message.SharedQueueName, message.Function.Id).ToString();
            Guid functionInstanceId = message.FunctionInstanceId;
            DateTimeOffset startTime = message.StartTime;
            WebJobRunIdentifier webJobRunId = message.WebJobRunIdentifier;
            Guid? parentId = message.ParentId;

            // Race to write index entries for function started.
            if (!HasLoggedFunctionCompleted(functionInstanceId))
            {
                CreateOrUpdateIndexEntries(snapshot, startTime, webJobRunId);
            }

            // If the function has since completed, we lost the race.
            // Delete any function started index entries.
            // Note that this code does not depend on whether or not the index entries were previously written by this
            // method, as this processing could have been aborted and resumed at another point in time. In that case,
            // we still own cleaning up any dangling function started index entries.
            DateTimeOffset? functionCompletedTime = GetFunctionCompletedTime(functionInstanceId);
            bool hasLoggedFunctionCompleted = functionCompletedTime.HasValue;

            if (hasLoggedFunctionCompleted)
            {
                DeleteFunctionStartedIndexEntriesIfNeeded(functionInstanceId, message.StartTime,
                    functionCompletedTime.Value, functionId, parentId, webJobRunId);
            }
        }

        private bool HasLoggedFunctionCompleted(Guid functionInstanceId)
        {
            DateTimeOffset? functionCompletedTime = GetFunctionCompletedTime(functionInstanceId);

            return functionCompletedTime.HasValue;
        }

        private DateTimeOffset? GetFunctionCompletedTime(Guid functionInstanceId)
        {
            FunctionInstanceSnapshot primaryLog = _functionInstanceLookup.Lookup(functionInstanceId);

            return primaryLog != null ? primaryLog.EndTime : null;
        }

        private void CreateOrUpdateIndexEntries(FunctionInstanceSnapshot snapshot, DateTimeOffset timestamp, WebJobRunIdentifier webJobRunId)
        {
            _recentInvocationsWriter.CreateOrUpdate(snapshot, timestamp);
            _recentInvocationsByFunctionWriter.CreateOrUpdate(snapshot, timestamp);

            if (webJobRunId != null)
            {
                _recentInvocationsByJobRunWriter.CreateOrUpdate(snapshot, webJobRunId, timestamp);
            }

            if (snapshot.ParentId.HasValue)
            {
                _recentInvocationsByParentWriter.CreateOrUpdate(snapshot, timestamp);
            }
        }

        private void DeleteFunctionStartedIndexEntriesIfNeeded(Guid functionInstanceId, DateTimeOffset startTime,
            DateTimeOffset endTime, string functionId, Guid? parentId, WebJobRunIdentifier webJobRunId)
        {
            if (startTime.UtcDateTime.Ticks != endTime.UtcDateTime.Ticks)
            {
                _recentInvocationsWriter.DeleteIfExists(startTime, functionInstanceId);
                _recentInvocationsByFunctionWriter.DeleteIfExists(functionId, startTime, functionInstanceId);

                if (parentId.HasValue)
                {
                    _recentInvocationsByParentWriter.DeleteIfExists(parentId.Value, startTime, functionInstanceId);
                }

                if (webJobRunId != null)
                {
                    _recentInvocationsByJobRunWriter.DeleteIfExists(webJobRunId, startTime, functionInstanceId);
                }
            }
        }

        // This method runs concurrently with other index processing.
        // Ensure all logic here is idempotent.
        public void ProcessFunctionCompleted(FunctionCompletedMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException("message");
            }

            FunctionInstanceSnapshot snapshot = CreateSnapshot(message);

            // The completed message includes the full parameter logs; delete the extra blob used for running status
            // updates.
            DeleteParameterLogBlob(snapshot.ParameterLogBlob);
            snapshot.ParameterLogBlob = null;

            _functionInstanceLogger.LogFunctionCompleted(snapshot);

            Guid functionInstanceId = message.FunctionInstanceId;
            DateTimeOffset endTime = message.EndTime;
            string functionId = new FunctionIdentifier(message.SharedQueueName, message.Function.Id).ToString();
            Guid? parentId = message.ParentId;
            WebJobRunIdentifier webJobRunId = message.WebJobRunIdentifier;

            DeleteFunctionStartedIndexEntriesIfNeeded(functionInstanceId, message.StartTime, endTime, functionId,
                parentId, webJobRunId);

            CreateOrUpdateIndexEntries(snapshot, endTime, webJobRunId);

            // Increment is non-idempotent. If the process dies before deleting the message that triggered it, it can
            // occur multiple times.
            // If we wanted to make this operation idempotent, one option would be to store the list of function
            // instance IDs that succeeded & failed, rather than just the counters, so duplicate operations could be
            // detected.
            // For now, we just do a non-idempotent increment last, which makes it very unlikely that the queue message
            // would not subsequently get deleted.
            if (message.Succeeded)
            {
                _statisticsWriter.IncrementSuccess(functionId);
            }
            else
            {
                _statisticsWriter.IncrementFailure(functionId);
            }
        }

        internal static FunctionInstanceSnapshot CreateSnapshot(FunctionStartedMessage message)
        {
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
            IDictionary<string, FunctionInstanceArgument> arguments = new Dictionary<string, FunctionInstanceArgument>();

            foreach (KeyValuePair<string, string> item in argumentValues)
            {
                string name = item.Key;
                ParameterDescriptor descriptor = GetParameterDescriptor(parameters, name);
                arguments.Add(name, CreateFunctionInstanceArgument(item.Value, descriptor));
            }

            return arguments;
        }

        internal static FunctionInstanceArgument CreateFunctionInstanceArgument(string value, ParameterDescriptor descriptor)
        {
            BlobParameterDescriptor blobDescriptor = descriptor as BlobParameterDescriptor;
            BlobTriggerParameterDescriptor blobTriggerDescriptor = null;

            string accountName = null;
            if (blobDescriptor != null)
            {
                accountName = blobDescriptor.AccountName;
            }
            else
            {
                blobTriggerDescriptor = descriptor as BlobTriggerParameterDescriptor;
                if (blobTriggerDescriptor != null)
                {
                    accountName = blobTriggerDescriptor.AccountName;
                }
            }

            return new FunctionInstanceArgument
            {
                Value = value,
                IsBlob = blobDescriptor != null || blobTriggerDescriptor != null,
                IsBlobOutput = blobDescriptor != null && blobDescriptor.Access == FileAccess.Write,
                AccountName = accountName
            };
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

        private void DeleteParameterLogBlob(LocalBlobDescriptor blobDescriptor)
        {
            if (blobDescriptor == null)
            {
                return;
            }

            CloudBlockBlob blob = blobDescriptor.GetBlockBlob(_cloudStorageAccount);
            blob.DeleteIfExists();
        }
    }
}
