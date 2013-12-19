namespace Microsoft.WindowsAzure.Jobs
{
    // This function was executed via an ICall interface. 
    internal class InvokeTriggerReason : TriggerReason
    {
        public override string ToString()
        {
            return this.Message;
        }

        public string Message { get; set; }
    }
}
