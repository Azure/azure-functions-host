using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WebJobs.Client
{
    public class WebJobClient
    {
        private readonly string _hostId;
        private readonly CloudQueue _hostMessageQueue;
        private readonly CloudBlobContainer _hostBlobContainer;
        private readonly TimeSpan _functionTimeout;

        public WebJobClient(string hostId, string storageConnectionString, TimeSpan functionTimeout)
        {
            _hostId = hostId;
            CloudStorageAccount account = CloudStorageAccount.Parse(storageConnectionString);
            _functionTimeout = functionTimeout;

            CloudQueueClient queueClient = account.CreateCloudQueueClient();
            string hostQueueName = string.Format("azure-webjobs-host-{0}", hostId);
            _hostMessageQueue = queueClient.GetQueueReference(hostQueueName);
            _hostMessageQueue.CreateIfNotExists();

            CloudBlobClient blobClient = account.CreateCloudBlobClient();
            string hostBlobContainerName = string.Format("azure-webjobs-hosts");
            _hostBlobContainer = blobClient.GetContainerReference(hostBlobContainerName);
            _hostBlobContainer.CreateIfNotExists();
        }

        public Guid InvokeFunction(string name, Dictionary<string, string> arguments)
        {
            Guid functionInstanceId = Guid.NewGuid();
            string functionId = string.Format("Host.Functions.{0}", name);
            JObject argumentsObject = new JObject();
            foreach (var argument in arguments)
            {
                argumentsObject.Add(argument.Key, argument.Value);
            }
            JObject message = new JObject()
            {
                { "Type", "CallAndOverride" },
                { "Id", functionInstanceId },
                { "FunctionId", functionId.ToString() },
                { "Arguments", argumentsObject },
                { "LogLevel", "Verbose" }
            };

            CloudQueueMessage queueMessage = new CloudQueueMessage(message.ToString(Formatting.None));
            _hostMessageQueue.AddMessage(queueMessage);

            return functionInstanceId;
        }

        public void WriteInvocationResults(string name, Guid functionInstanceId)
        {
            var holdColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkCyan;

            try
            {
                Console.WriteLine("Queued: {0}", DateTime.Now.TimeOfDay);

                string functionStatusBlobName = string.Format("invocations/{0}/Host.Functions.{1}/{2}", _hostId, name, functionInstanceId);

                CloudBlockBlob resultBlob = _hostBlobContainer.GetBlockBlobReference(functionStatusBlobName);
                DateTime start = DateTime.Now;
                bool resultBlobExists = false;
                bool wroteStart = false;
                while ((DateTime.Now - start) < _functionTimeout)
                {
                    if (!resultBlobExists && !resultBlob.Exists())
                    {
                        Thread.Sleep(500);
                        continue;
                    }

                    resultBlobExists = true;
                    string text = resultBlob.DownloadText();
                    JObject result = JObject.Parse(text);

                    string status = (string)result["Status"];
                    bool completed = string.Compare("completed", status, StringComparison.OrdinalIgnoreCase) == 0;
                    if (!wroteStart)
                    {
                        Console.WriteLine(string.Format("{0} {1}", "Started: ", ((DateTime)result["StartTime"]).TimeOfDay));
                        wroteStart = true;
                    }
                    if (!completed)
                    {
                        continue;
                    }

                    JObject failure = (JObject)result["Failure"];
                    string resultPrefix = failure != null ? "Failed: " : "Completed: ";

                    Console.WriteLine(string.Format("{0} {1}", resultPrefix, ((DateTime)result["EndTime"]).TimeOfDay, resultPrefix));
                    Console.WriteLine(string.Format("Total Time: {0}", ((DateTime)result["EndTime"]) - ((DateTime)result["StartTime"])));

                    if (failure != null)
                    {
                        WriteWithColor((string)failure["ExceptionType"], ConsoleColor.Red);
                        string exceptionDetails = (string)failure["ExceptionDetails"];
                        WriteWithColor(exceptionDetails.Trim(), ConsoleColor.Red);
                    }
                    else
                    {
                        string outputBlobName = (string)result["OutputBlob"]["BlobName"];
                        CloudBlockBlob outputBlob = _hostBlobContainer.GetBlockBlobReference(outputBlobName);
                        if (outputBlob.Exists())
                        {
                            string output = outputBlob.DownloadText();
                            Console.WriteLine("Output: " + output.Trim());
                        }
                    }
                    
                    return;
                }

                if (!resultBlob.Exists())
                {
                    WriteWithColor("Unable to get result before timeout expired.", ConsoleColor.Yellow);
                }
            }
            finally
            {
                Console.ForegroundColor = holdColor;
            }
        }

        public static void WriteWithColor(string text, ConsoleColor color)
        {
            var holdColor = Console.ForegroundColor;

            Console.ForegroundColor = color;
            Console.WriteLine(text);

            Console.ForegroundColor = holdColor;
        }
    }
}
