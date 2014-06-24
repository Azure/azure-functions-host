using System;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Azure.Jobs.Host.Blobs.Bindings;
using Microsoft.Azure.Jobs.Host.Blobs.Triggers;
using Microsoft.Azure.Jobs.Host.Indexers;
using Microsoft.Azure.Jobs.Host.Queues.Triggers;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs
{
    internal class CalculateTriggers
    {
        public static Trigger GetTrigger(FunctionDefinition func, Credentials credentials)
        {
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
            BlobTriggerBinding blobTriggerBinding = func.TriggerBinding as BlobTriggerBinding;

            if (blobTriggerBinding != null)
            {
                return TriggerRaw.NewBlob(blobTriggerBinding.BlobPath, GetOutputPath(func));
            }

            return null; // No triggers
        }

        // Get the BlobOutput paths for a function. 
        private static string GetOutputPath(FunctionDefinition func)
        {
            StringBuilder sb = null;

            foreach (var binding in func.NonTriggerBindings.Values)
            {
                var x = binding as BlobBinding;
                if (x != null && x.Access == FileAccess.Write)
                {
                    if (sb == null)
                    {
                        sb = new StringBuilder();
                    }
                    else
                    {
                        sb.Append(';');
                    }
                    sb.Append(x.BlobPath);
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
