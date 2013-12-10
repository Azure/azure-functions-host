using System;
using System.Collections.Generic;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;

namespace Executor
{
    // Help executors queue a "FunctionCompleted" notification and Orchestrator reads it.
    // This keeps the Enqueue and Dequeue operation coupled together.
    // By queueing it, it also serializes write operations (accumulated from multiple executors) and makes them single threaded. 
    public class ExecutionStatsAggregatorBridge : IFunctionCompleteLogger
    {
        private readonly CloudQueue _queue;

        public ExecutionStatsAggregatorBridge(CloudQueue queue)
        {
            _queue = queue;
        }

        public class ExecutionFinishedPayload
        {
            // FunctionInstance Ids for functions that we just finished. 
            // Orchestrator cna look these up and apply the deltas. 
            public Guid[] Instances { get; set; }
        }

        // Called by many execution nodes
        public void IndexCompletedFunction(ExecutionInstanceLogEntity instance)
        {
            // If complete, then queue a message to the orchestrator so it can aggregate stats. 
            if (instance.IsCompleted())
            {
                var json = JsonCustom.SerializeObject(new ExecutionFinishedPayload { Instances = new Guid[] { instance.FunctionInstance.Id } });
                CloudQueueMessage msg = new CloudQueueMessage(json);
                _queue.AddMessage(msg);
            }
        }

        // Called by single Orchestrator node to aggregate
        // Will apply each function to the stats object.
        public IEnumerable<Guid> DrainQueue()
        {
            while (true)
            {
                var msg = _queue.GetMessage();
                if (msg == null)
                {
                    break;
                }

                _queue.DeleteMessage(msg);

                var payload = JsonCustom.DeserializeObject<ExecutionFinishedPayload>(msg.AsString);
                foreach (var instance in payload.Instances)
                {
                    yield return instance;
                }
            }
        }

        void IFunctionCompleteLogger.Flush()
        {            
            // Nop since function is queued immediately. 
            // @@@ When Orch listens on this, it calls IPrereqManager too. 
        }
    }
}