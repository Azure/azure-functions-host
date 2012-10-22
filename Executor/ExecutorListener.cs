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

    public interface IExecutionLogger
    {
        // Returns TextWriter and a "done" action.
        FunctionOutputLog GetLogStream(FunctionInstance f);

        void LogFatalError(string info, Exception e);

        // Called to update the function instance.  This can be called multiple times as the function progresses.
        void UpdateInstanceLog(ExecutionInstanceLogEntity instance);

        void WriteHeartbeat(ExecutionRoleHeartbeat stats);

        bool IsDeleteRequested(Guid id);
    }

    public class FunctionOutputLog
    {
        static Action empty = () => { };

        public FunctionOutputLog()
        {
            this.Output = Console.Out;
            this.CloseOutput = empty;
        }

        public TextWriter Output { get; set ; }
        public Action CloseOutput { get; set; }
        public string Uri { get; set; } // Uri to refer to output 

        // Separate channel for logging structured (and updating) information about parameters
        public CloudBlobDescriptor ParameterLogBlob { get; set; }
    }

    // Default logger, just goes to console.
    public class EmptyExecutionLogger : IExecutionLogger
    {
        public FunctionOutputLog GetLogStream(FunctionInstance f)
        {
            return new FunctionOutputLog();
        }

        public void LogFatalError(string info, Exception e)
        {            
        }

        public void UpdateInstanceLog(ExecutionInstanceLogEntity instance)
        {            
        }
        

        public void WriteHeartbeat(ExecutionRoleHeartbeat stats)
        {
            
        }

        public bool IsDeleteRequested(Guid id)
        {
            return false;
        }
    }

    // Listens on a queue, deques, and runs
    public class ExecutorListener : IDisposable
    {
        readonly private Executor _executor;
        readonly private ExecutionQueueSettings _queueSettings;

        public ExecutorListener(string localCache, ExecutionQueueSettings queueSettings)
        {
            _queueSettings = queueSettings;
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
            CloudQueue q = _queueSettings.GetQueue();

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
            FunctionInstance instance = JsonCustom.DeserializeObject<FunctionInstance>(json);

            _stats.FunctionInstanceId = instance.Id;
            WriteHeartbeat(logger);
            
            FunctionOutputLog logInfo = logger.GetLogStream(instance);
            instance.ParameterLogBlob = logInfo.ParameterLogBlob;
                
            // Log functions for later mining. 
            ExecutionInstanceLogEntity logItem = new ExecutionInstanceLogEntity();                
                
            logItem.FunctionInstance = instance;
            logItem.OutputUrl = logInfo.Uri; // URL can now be written to incrementally 
            logItem.StartTime = DateTime.UtcNow;
            logger.UpdateInstanceLog(logItem);
                
            int maxDequeueCount = 3;
            if (msg.DequeueCount > maxDequeueCount)
            {
                // Handle poison messages. We should be robust to all the cases, so this shouldn't happen. 
                logItem.ExceptionType = "DequeueCountExceeded";
                logItem.ExceptionMessage = string.Format("Function could not be executed after {0} attempts. Giving up.", maxDequeueCount);
            }
            else
            {

                Stopwatch sw = new Stopwatch(); // Provide higher resolution timer for function
                sw.Start();
                try
                {
                    var result = _executor.Execute(instance, logInfo.Output, token);

                    // User errors should be caught and returned in result message.

                    logItem.ExceptionType = result.ExceptionType;
                    logItem.ExceptionMessage = result.ExceptionMessage;
                }
                catch (OperationCanceledException e)
                {
                    // Execution was aborted. Common case, not a critical error. 
                    logItem.ExceptionType = e.GetType().FullName;
                    logItem.ExceptionMessage = e.Message;
                }
                catch (Exception e)
                {
                    // Non-user error. Something really bad happened.                    
                    LogCriticalError(logger, e);

                    logInfo.Output.WriteLine("Error: {0}", e.Message);
                    logInfo.Output.WriteLine("stack: {0}", e.StackTrace);
                }
                logInfo.CloseOutput();
                sw.Stop();
            }

            // Log completion information
            logItem.EndTime = DateTime.UtcNow;
            logger.UpdateInstanceLog(logItem);                            
        }

        public void Dispose()
        {
            this._executor.Dispose(); // clears the cache.
        }
    }
}
