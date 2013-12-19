namespace Microsoft.WindowsAzure.Jobs.Dashboard.Models.Protocol
{
    public class CloudBlobDescriptorModel
    {
        internal CloudBlobDescriptor UnderlyingObject { get; private set; }

        internal CloudBlobDescriptorModel(CloudBlobDescriptor underlyingObject)
        {
            UnderlyingObject = underlyingObject;
        }

        public string AccountConnectionString { get { return UnderlyingObject.AccountConnectionString; } }

        public string ContainerName { get { return UnderlyingObject.ContainerName; } }

        public override string ToString()
        {
            return UnderlyingObject.ToString();
        }
    }
}
