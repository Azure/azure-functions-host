namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class HttpBindingMetadata : BindingMetadata
    {
        public string Route { get; set; }
        public string WebHookReceiver { get; set; }
    }
}
