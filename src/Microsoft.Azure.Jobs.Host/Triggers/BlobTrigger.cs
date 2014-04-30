using System.Text;

namespace Microsoft.Azure.Jobs
{
    internal class BlobTrigger : Trigger
    {
        public BlobTrigger()
        {
            this.Type = TriggerType.Blob;
        }

        public CloudBlobPath BlobInput { get; set; }

        // list of output blobs. Null if no outputs. 
        // Don't fire trigger if all ouptuts are newer than the input. 
        public CloudBlobPath[] BlobOutputs { get; set; }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("Trigger on {0}", BlobInput);
            if (BlobOutputs != null)
            {
                sb.AppendFormat(" unless {0} is newer", string.Join<CloudBlobPath>(";", BlobOutputs));
            }
            return sb.ToString();
        }
    }
}
