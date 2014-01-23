namespace Microsoft.WindowsAzure.Jobs
{
    internal class QueueTrigger : Trigger
    {
        public QueueTrigger()
        {
            this.Type = TriggerType.Queue;
        }

        public string QueueName { get; set; }

        public override string ToString()
        {
            return string.Format("Trigger on queue {0}", QueueName);
        }
    }
}
