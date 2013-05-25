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
            Post(func.CallbackPath, token);
        }

        public void OnNewQueueItem(CloudQueueMessage msg, QueueTrigger func, CancellationToken token)
        {
            byte[] contents = msg.AsBytes;
            Post(func.CallbackPath, token, contents);
        }

        private void Post(string url, CancellationToken token, byte[] contents = null)
        {
            if (contents == null)
            {
                contents = new byte[0];
            }

            try
            {
                HttpClient c = new HttpClient();
                var result = c.PostAsync(url, new ByteArrayContent(contents), token);
                HttpResponseMessage response = result.Result;
            }
            catch
            {
                // $$$ What ot do about errors? Bad user URL?
            }
        }
    }
}