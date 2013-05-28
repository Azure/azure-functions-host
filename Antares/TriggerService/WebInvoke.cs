using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;

namespace TriggerService
{
    public class WebInvoke : ITriggerInvoke
    {
        public void OnNewTimer(TimerTrigger func, CancellationToken token)
        {
            Post(func.CallbackPath, token);
        }

        public void OnNewBlob(CloudBlob blob, BlobTrigger func, CancellationToken token)
        {
            // $$$ Have to provide the blob name. Do it as NVC?
            string containerName = blob.Container.Name;
            string blobName = blob.Name;
            string name = containerName + "/" + blobName;
            var content = new StringContent(name);

            Post(func.CallbackPath, token, content);
        }

        public void OnNewQueueItem(CloudQueueMessage msg, QueueTrigger func, CancellationToken token)
        {
            byte[] bytes = msg.AsBytes;
            var content = new ByteArrayContent(bytes);
            Post(func.CallbackPath, token, content);
        }

        private void Post(string url, CancellationToken token, HttpContent content = null)
        {
            if (content == null)
            {
                content = new ByteArrayContent(new byte[0]);
            }

            try
            {
                HttpClient c = new HttpClient();
                var result = c.PostAsync(url, content, token);
                HttpResponseMessage response = result.Result;
            }
            catch
            {
                // $$$ What ot do about errors? Bad user URL?
            }
        }
    }
}