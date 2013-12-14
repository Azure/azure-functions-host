using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    /*
    public class WebInvoke : ITriggerInvoke
    {
        public void OnNewTimer(TimerTrigger func, CancellationToken token)
        {
            HttpRequestMessage msg = CreateTimerHttpRequestMessage(func);
            Send(msg, token);            
        }

        public void OnNewBlob(CloudBlob blob, BlobTrigger func, CancellationToken token)
        {
            HttpRequestMessage msg = CreateBlobHttpRequestMessage(blob, func);
            Send(msg, token);
        }

        public void OnNewQueueItem(CloudQueueMessage queueMessage, QueueTrigger func, CancellationToken token)
        {
            HttpRequestMessage msg = CreateQueueHttpRequestMessage(queueMessage, func);
            Send(msg, token);
        }

        public static HttpRequestMessage CreateTimerHttpRequestMessage(TimerTrigger func)
        {
            HttpRequestMessage msg = NewMessage(func, null);
            return msg;
        }

        public static HttpRequestMessage CreateBlobHttpRequestMessage(CloudBlob blob, BlobTrigger func)
        {
            // BlobInput path may have routing args, like Container/{name}.txt,
            // so we need to provide the actual input so client can bind 'name'. 

            // Provide blob name in body. 
            string containerName = blob.Container.Name;
            string blobName = blob.Name;
            string name = containerName + "/" + blobName;
            var content = new StringContent(name);

            HttpRequestMessage msg = NewMessage(func, content);
            return msg;
        }

        public static HttpRequestMessage CreateQueueHttpRequestMessage(CloudQueueMessage queueMessage, QueueTrigger func)
        {
            byte[] bytes = queueMessage.AsBytes;
            var content = new ByteArrayContent(bytes);

            HttpRequestMessage msg = NewMessage(func, content);
            return msg;
        }

        private static HttpRequestMessage NewMessage(Trigger func, HttpContent content)
        {
            if (content == null)
            {
                content = new ByteArrayContent(new byte[0]); // empty
            }
            HttpRequestMessage msg = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(func.CallbackPath),
                Content = content
            };
            return msg;
        }

        private static void Send(HttpRequestMessage msg, CancellationToken token)
        {
            try
            {
                HttpClient c = new HttpClient();
                var response = c.SendAsync(msg, token).Result;
            }
            catch
            {
                // Ignore errors. Bad Url? Bad response?
            }
        }

    }
     * */
}