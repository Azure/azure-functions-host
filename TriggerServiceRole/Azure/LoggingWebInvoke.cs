using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using Microsoft.WindowsAzure.StorageClient;
using TriggerService;

namespace TriggerServiceRole
{
    // Log each invoke. 
    // Log failures. (bad url? bad response), timing. 
    class LoggingWebInvoke : ITriggerInvoke
    {
        public void OnNewTimer(TimerTrigger func, CancellationToken token)
        {
            var msg = WebInvoke.CreateTimerHttpRequestMessage(func);
            string details = string.Format("Timer invoked @ {0}", func.Interval);
            Send(msg, details, token);
        }

        public void OnNewQueueItem(CloudQueueMessage queueMessage, QueueTrigger func, CancellationToken token)
        {
            var msg = WebInvoke.CreateQueueHttpRequestMessage(queueMessage, func);
            string details = string.Format("New queue invoked {0}", func.QueueName);
            Send(msg, details, token);
        }

        public void OnNewBlob(CloudBlob blob, BlobTrigger func, CancellationToken token)
        {
            var msg = WebInvoke.CreateBlobHttpRequestMessage(blob, func);
            string details = string.Format("New blob input detected {0} ({1})", func.BlobInput, blob.Name);
            Send(msg, details, token);
        }

        private static void Send(HttpRequestMessage msg, string details, CancellationToken token)
        {
            Stopwatch sw = Stopwatch.StartNew();
            string status;
            try
            {
                try
                {
                    HttpClient c = new HttpClient();
                    var response = c.SendAsync(msg, token).Result;
                }
                finally
                {
                    sw.Stop();
                }
                status = "ok";
                // Success!
            }
            catch (AggregateException e)
            {
                var e2 = e.InnerExceptions[0];
                status = "failed: " + e2.Message;
            }
            catch (Exception e)
            {
                // Failure 
                status = "failed: " + e.Message;
            }

            // Ignore errors. Bad Url? Bad response?
            string log = string.Format("{0}, {1}, {2}, {3}", details, msg.RequestUri, sw.ElapsedMilliseconds, status);
            TriggerConfig.AppendLog(log);
        }
    }
}