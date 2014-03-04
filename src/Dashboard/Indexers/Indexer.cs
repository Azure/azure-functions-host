using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Jobs;
using Microsoft.WindowsAzure.Jobs.Host.Protocols;

namespace Dashboard.Indexers
{
    internal class Indexer : IIndexer
    {
        private readonly IPersistentQueue<HostStartupMessage> _queue;
        private readonly IFunctionTable _functionTable;

        public Indexer(IPersistentQueue<HostStartupMessage> queue, IFunctionTable functionTable)
        {
            _queue = queue;
            _functionTable = functionTable;
        }

        public void Update()
        {
            HostStartupMessage message = _queue.Dequeue();

            while (message != null)
            {
                Process(message);
                _queue.Delete(message);

                message = _queue.Dequeue();
            }
        }

        private void Process(HostStartupMessage message)
        {
            Guid hostInstanceId = message.HostInstanceId;

            IEnumerable<FunctionDefinition> newFunctions = message.Functions;

            Guid hostId = message.HostId;

            IEnumerable<FunctionDefinition> existingFunctions = _functionTable.ReadAll().Where(f => f.HostId == hostId);

            DateTime hostInstanceCreatedOn = message.EnqueuedOn;

            IEnumerable<FunctionDefinition> functionsToDelete = existingFunctions.Where(f => !f.HostVersion.HasValue || f.HostVersion.Value < hostInstanceCreatedOn);

            foreach (FunctionDefinition deleteFunction in functionsToDelete)
            {
                _functionTable.Delete(deleteFunction);
            }

            DateTime mostRecentHostInstanceDate = existingFunctions.Where(f => f.HostVersion.HasValue).Select(f => f.HostVersion.Value).OrderByDescending(d => d).FirstOrDefault();

            if (hostInstanceCreatedOn > mostRecentHostInstanceDate)
            {
                foreach (FunctionDefinition functionToAdd in newFunctions)
                {
                    functionToAdd.HostId = hostId;
                    functionToAdd.HostVersion = hostInstanceCreatedOn;
                    _functionTable.Add(functionToAdd);
                }
            }
        }
    }
}