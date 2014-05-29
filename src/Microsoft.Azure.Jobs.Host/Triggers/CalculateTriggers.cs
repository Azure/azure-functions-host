using System;
using System.Reflection;
using System.Text;
using Microsoft.Azure.Jobs.Host.Blobs.Triggers;
using Microsoft.Azure.Jobs.Host.Queues.Triggers;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs
{
    internal class CalculateTriggers
    {
        private static readonly Func<ParameterStaticBinding, TriggerRaw> _tryGetServiceBusTriggerRaw = _ => null;

        static CalculateTriggers()
        {
            var type = ServiceBusExtensionTypeLoader.Get("Microsoft.Azure.Jobs.CalculateServiceBusTriggers");
            if (type == null)
            {
                return;
            }

            var getTriggerMethod = type.GetMethod("GetTriggerRaw");
            _tryGetServiceBusTriggerRaw = binding => getTriggerMethod.Invoke(null, new object[] {binding}) as TriggerRaw;
        }

        public static Trigger GetTrigger(FunctionDefinition func)
        {
            var credentials = new Credentials
            {
                 StorageConnectionString = func.Location.StorageConnectionString,
                 ServiceBusConnectionString = func.Location.ServiceBusConnectionString
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
            QueueTriggerBinding queueTriggerBinding = func.TriggerBinding as QueueTriggerBinding;

            if (queueTriggerBinding != null)
            {
                return TriggerRaw.NewQueue(null, queueTriggerBinding.QueueName);
            }

            BlobTriggerBinding blobTriggerBinding = func.TriggerBinding as BlobTriggerBinding;

            if (blobTriggerBinding != null)
            {
                return TriggerRaw.NewBlob(null, blobTriggerBinding.BlobPath, GetOutputPath(func));
            }

            var flow = func.Flow;
            foreach (var input in flow.Bindings)
            {
                var serviceBusTrigger = _tryGetServiceBusTriggerRaw(input);
                if (serviceBusTrigger != null)
                {
                    return serviceBusTrigger;
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
