using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;
using TriggerService;

namespace Orchestrator
{
    class CalculateTriggers
    {
        // Given a function definition, get the set of Triggers from it. 
        public static Trigger GetTrigger(FunctionDefinition func)
        {
            var trigger = func.Trigger;

            if (trigger.TimerInterval.HasValue)
            {
                return new TimerTrigger
                {
                    Tag = func,
                    Interval = trigger.TimerInterval.Value
                };
            }

            var flow = func.Flow;
            foreach (var input in flow.Bindings)
            {
                if (trigger.ListenOnBlobs)
                {
                    var blobBinding = input as BlobParameterStaticBinding;
                    if (blobBinding != null)
                    {
                        if (!blobBinding.IsInput)
                        {
                            continue;
                        }
                        CloudBlobPath path = blobBinding.Path;
                        string containerName = path.ContainerName;

                        // Check if it's on the ignore list
                        var account = func.GetAccount();

                        CloudBlobClient clientBlob = account.CreateCloudBlobClient();
                        CloudBlobContainer container = clientBlob.GetContainerReference(containerName);
                                                
                        return new BlobTrigger
                        {
                            AccountConnectionString = func.Location.AccountConnectionString,
                            Tag = func,
                            BlobInput = path.ToString(),
                            BlobOutput = GetOutputPath(func)
                        };
                    }
                }

                var queueBinding = input as QueueParameterStaticBinding;
                if (queueBinding != null)
                {
                    if (queueBinding.IsInput)
                    {
                        // Queuenames must be all lowercase. Normalize for convenience. 
                        string queueName = queueBinding.QueueName.ToLower();

                        return new QueueTrigger
                        {
                            AccountConnectionString = func.Location.AccountConnectionString,
                            QueueName = queueName,
                            Tag = func,
                        };
                    }
                }
            }
            return null; // No triggers
        }

        // Get the BlobOutput paths for a function. 
        private static string GetOutputPath(FunctionDefinition func)
        {
            StringBuilder sb = null;

            foreach (var staticBinding in func.Flow.Bindings)
            {
                var x = staticBinding as BlobParameterStaticBinding;
                if (x != null)
                {
                    if (!x.IsInput)
                    {
                        if (sb == null)
                        {
                            sb = new StringBuilder();
                        }
                        else
                        {
                            sb.Append(';');
                        }
                        sb.Append(x.Path);
                    }
                }
            }

            if (sb == null)
            {
                return null;
            }
            return sb.ToString();
        }
    }
}
