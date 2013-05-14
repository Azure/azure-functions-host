using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using RunnerInterfaces;

namespace Executor
{
    // health information written by an execution role. 
    public class ExecutionRoleHeartbeat
    {
        // Function instance ID that we're currently processing. 
        public Guid? FunctionInstanceId { get; set; }

        // Times are UTC. 
        public DateTime Uptime { get; set; } // when this node went up
        public DateTime LastCacheReset { get; set; } // when were the caches last reset
        public DateTime Heartbeat { get; set; } // last time written

        public int RunCount { get; set; }
        public int CriticalErrors { get; set; }
        
    }

    // FunctionExecutionContext is the common execution operations that aren't Worker-role specific.
    // Everything else is worker role specific. 
    public interface IExecutionLogger
    {
        FunctionExecutionContext GetExecutionContext();

        void LogFatalError(string info, Exception e);
                
        // Write health status for the worker role. 
        void WriteHeartbeat(ExecutionRoleHeartbeat stats);

        // Check if a delete is requested and then set a Cancellation token 
        // The communication here could be from detecting a blob; or it could be from WorkerRole-WorkerRole communication.
        bool IsDeleteRequested(Guid id);
    }    

    // Listens on a queue, deques, and runs
    public class ExecutorListener : IDisposable
    {
        readonly private Executor _executor;
        readonly private CloudQueue _executionQueue;

        public ExecutorListener(string localCache, CloudQueue executionQueue)
        {
            _executionQueue = executionQueue;
            _executor = new Executor(localCache);
        }

        

        public void Run(IExecutionLogger logger)
        {
            while (true)
            {
                Poll(logger);
                // None available. Sleep. 
                Thread.Sleep(2 * 1000);
            }
        }

        // Return true if it did work. False if nothing happened (which can trigger the caller to sleep before retry)
        public bool Poll(IExecutionLogger logger)
        {
            CloudQueue q = _executionQueue;

#if true
            TimeSpan refreshRate = TimeSpan.FromMinutes(1); // renew at this rate. 
            TimeSpan visibilityTimeout = TimeSpan.FromMinutes(10);  
#else
            TimeSpan refreshRate = TimeSpan.FromSeconds(20); 
            TimeSpan visibilityTimeout = TimeSpan.FromSeconds(45);  
#endif

            var msg = q.GetMessage(visibilityTimeout);

            if (msg == null)
            {
                _stats.FunctionInstanceId = null;
                WriteHeartbeat(logger);
                return false;                    
            }

            CancellationTokenSource source = new CancellationTokenSource();

            bool done = false;
            try
            {
                using (new Timer(_ =>
                {
                    if (done)
                    {
                        return;
                    }

                    // Renews the message lease while we're still working on it. 
                    // This can fail if message expired on us. 
                    // 1. We didn't renew the least fast enough. 
                    //   In that case, it can become visible again and another node may pull it out of the queue.
                    //   So it's imperative that the heart beat timer stay alive (and that we don't put any other
                    //   long running work on this thread)
                    // 2. The main thread finished and called DeleteMessage
                    try
                    {
                        q.UpdateMessage(msg, visibilityTimeout, MessageUpdateFields.Visibility);
                    }
                    catch (Exception e2)
                    {
                        done = true; // Ignore further timer requests

                        // Take down this node to stop further execution. Another node will pick it up.
                        LogCriticalError(logger, e2, "Message expired in queue");

                        // Beware! Azure Compute emulator doesn't recycle roles. 
                        WriteHeartbeat(logger);
                        Environment.Exit(1);
                    }

                    WriteHeartbeat(logger);


                    // Check for delete request.
                    if (CheckForDelete(logger))
                    {
                        done = true;

                        // This should leverage regular logging mechanisms.
                        // Ok for this to cause role to shutdown and recycle.
                        // Will cause OperationCanceledException to get thrown. 
                        source.Cancel(); 

                        q.DeleteMessage(msg); // this may throw.

                        return;
                    }

                }, null, refreshRate, refreshRate)) // end timer function
                {
                    HandleMessage(msg, logger, source.Token);
                    done = true;
                }
            }            
            catch (Exception e)
            {
                LogCriticalError(logger, e);
            }

            try
            {
                q.DeleteMessage(msg);
            }
            catch
            {
                // Already deleted.
            }

            return true;
        }

        private bool CheckForDelete(IExecutionLogger logger)
        {
            if (_stats.FunctionInstanceId.HasValue)
            {
                var id = _stats.FunctionInstanceId.Value;
                if (logger.IsDeleteRequested(id))
                {
                    return true;
                }
            }
            return false;
        }

        ExecutionRoleHeartbeat _stats = new ExecutionRoleHeartbeat();

        public ExecutionRoleHeartbeat Stats
        {
            get { return _stats; } 
        }

        private void WriteHeartbeat(IExecutionLogger logger)
        {
            _stats.Heartbeat = DateTime.UtcNow;
            logger.WriteHeartbeat(_stats);
        }

        private void LogCriticalError(IExecutionLogger logger, Exception e, string text = null)
        {
            text = text ?? "Critical error!";
            logger.LogFatalError(text, e);
            this._stats.CriticalErrors++;
        }
         
        private void HandleMessage(CloudQueueMessage msg, IExecutionLogger logger, CancellationToken token)
        {                       
            _stats.RunCount++;

            string json = msg.AsString;
            FunctionInvokeRequest instance = JsonCustom.DeserializeObject<FunctionInvokeRequest>(json);


            if (instance.SchemaNumber != FunctionInvokeRequest.CurrentSchema)
            {
                // Ignore any stale messages. 
                // These could have been reactivated messages froma prior service instance.
                return;
            }

            _stats.FunctionInstanceId = instance.Id;
            WriteHeartbeat(logger);
            
            // Call 
            Func<TextWriter, FunctionExecutionResult> fpInvokeFunc = (consoleOutput) =>
                {
                    int maxDequeueCount = 3;
                    if (msg.DequeueCount > maxDequeueCount)
                    {
                        // Handle poison messages. We should be robust to all the cases, so this shouldn't happen. 
                        return new FunctionExecutionResult
                        {
                            ExceptionType = "DequeueCountExceeded",
                            ExceptionMessage = string.Format("Function could not be executed after {0} attempts. Giving up.", maxDequeueCount)
                        };
                    }

                    var result = _executor.Execute(instance, consoleOutput, token);
                    return result;
                };
            
            var ctx = logger.GetExecutionContext();

            try
            {
                ExecutionBase.Work(
                    instance,
                    ctx,
                    fpInvokeFunc);
            }
            catch (Exception e)
            {
                LogCriticalError(logger, e);
            }
        }

        public void Dispose()
        {
            this._executor.Dispose(); // clears the cache.
        }
    }
}
