using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Azure.Jobs.Host.Blobs.Bindings;
using Microsoft.Azure.Jobs.Host.Blobs.Triggers;

namespace Microsoft.Azure.Jobs
{
    internal class BlobTrigger : Trigger
    {
        public BlobTrigger()
        {
            this.Type = TriggerType.Blob;
        }

        public IBlobPathSource BlobInput { get; set; }

        // list of output blobs. Null if no outputs. 
        // Don't fire trigger if all ouptuts are newer than the input. 
        public IBindableBlobPath[] BlobOutputs { get; set; }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("Trigger on {0}", BlobInput);
            if (BlobOutputs != null)
            {
                sb.AppendFormat(" unless {0} is newer", string.Join<IBindableBlobPath>(";", BlobOutputs));
            }
            return sb.ToString();
        }
    }
}
