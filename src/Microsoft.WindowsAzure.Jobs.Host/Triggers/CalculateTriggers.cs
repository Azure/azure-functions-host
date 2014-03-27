using System.Text;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class CalculateTriggers
    {
        public static Trigger GetTrigger(FunctionDefinition func)
        {
            var credentials = new Credentials
            {
                 AccountConnectionString = func.Location.AccountConnectionString
            };

            var raw = GetTriggerRaw(func);
            if (raw != null)
            {
                var x = Trigger.FromWire(raw, credentials);
                x.Tag = func;
                return x;
            }
            return null;
        }

        // Given a function definition, get the set of Triggers from it. 
        public static TriggerRaw GetTriggerRaw(FunctionDefinition func)
        {
            var trigger = func.Trigger;

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

                        return TriggerRaw.NewBlob(null, path.ToString(), GetOutputPath(func));                        
                    }
                }

                var queueBinding = input as QueueParameterStaticBinding;
                if (queueBinding != null)
                {
                    if (queueBinding.IsInput)
                    {
                        // Queuenames must be all lowercase. Normalize for convenience. 
                        string queueName = queueBinding.QueueName.ToLower();

                        return TriggerRaw.NewQueue(null, queueName);
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
