namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class TimerBindingMetadata : BindingMetadata
    {
        public string Schedule { get; set; }

        public bool RunOnStartup { get; set; }
    }
}
