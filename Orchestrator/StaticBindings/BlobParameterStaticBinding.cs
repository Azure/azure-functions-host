using System.Collections.Generic;
using System.IO;
using Microsoft.WindowsAzure;
using RunnerHost;
using RunnerInterfaces;

namespace Orchestrator
{
    // Side-effects are understood. We'll read/write to a specific blob, 
    // for which we can even get a modification timestamp from.
    public class BlobParameterStaticBinding : ParameterStaticBinding
    {
        public CloudBlobPath Path;
        public bool IsInput;

        public override ParameterRuntimeBinding Bind(RuntimeBindingInputs inputs)
        {
            // Bind to a blob container
            var path = this.Path;

            if (path.BlobName == null)
            {
                // Just a container match. Match to the input blob.
                path = new CloudBlobPath(inputs._blobInput);
            }
            else
            {
                path = path.ApplyNames(inputs._nameParameters);
            }

            var arg = new CloudBlobDescriptor
            {
                AccountConnectionString = Utility.GetConnectionString(inputs._account),
                ContainerName = path.ContainerName,
                BlobName = path.BlobName
            };
            return new BlobParameterRuntimeBinding { Blob = arg };
        }

        public override ParameterRuntimeBinding BindFromInvokeString(CloudStorageAccount account, string invokeString)
        {
            var path = new CloudBlobPath(invokeString);
            var arg = new CloudBlobDescriptor
            {
                AccountConnectionString = Utility.GetConnectionString(account),
                ContainerName = path.ContainerName,
                BlobName = path.BlobName
            };
            return new BlobParameterRuntimeBinding { Blob = arg };
        }

        public override string Description
        {
            get
            {
                if (IsInput)
                {
                    return string.Format("Read from blob: {0}", Path);
                }
                else
                {
                    return string.Format("Write to blob: {0}", Path);
                }
            }
        }

        public override IEnumerable<string> ProducedRouteParameters
        {
            get
            {
                return Path.GetParameterNames();
            }
        }

        public override TriggerType GetTriggerType()
        {
            if (this.IsInput)
            {
                return TriggerType.Input;
            }
            else
            {
                return TriggerType.Output;
            }
        }
    }
}