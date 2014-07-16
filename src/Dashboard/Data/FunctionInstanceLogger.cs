// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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

        public void LogFunctionQueued(FunctionInstanceSnapshot snapshot)
        {
            // Ignore the return result. Only the dashboard calls this method, and it does so before enqueuing the
            // message to the host to run the function. So realistically the blob can't already exist. And even if the
            // host did see the message before this call, an existing blob just means something more recent than
            // "queued" status, so there's nothing to do here in that case anyway.
            _store.TryCreate(GetId(snapshot), snapshot);
        }

        public void LogFunctionStarted(FunctionInstanceSnapshot snapshot)
        {
            // Which operation to run depends on whether or not the entity currently exists in the "queued" status.
            IConcurrentDocument<FunctionInstanceSnapshot> existingSnapshot = _store.Read(GetId(snapshot));

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

        public void LogFunctionCompleted(FunctionInstanceSnapshot snapshot)
        {
            // LogFunctionStarted and LogFunctionCompleted may run concurrently. Ensure LogFunctionCompleted wins by
            // having it replace the LogFunctionStarted record, if any.
            _store.CreateOrUpdate(GetId(snapshot), snapshot);
        }

        private static string GetId(FunctionInstanceSnapshot snapshot)
        {
            return snapshot.Id.ToString("N");
        }
    }
}
