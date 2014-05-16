using Microsoft.Azure.Jobs.Host.Protocols;

namespace Dashboard.Data
{
    [JsonTypeName("Queue")]
    internal class QueueParameterSnapshot : ParameterSnapshot
    {
        public string QueueName { get; set; }

        public bool IsInput { get; set; }

        public override string Description
        {
            get
            {
                if (this.IsInput)
                {
                    return string.Format("dequeue from '{0}'", this.QueueName);
                }
                else
                {
                    return string.Format("enqueue to '{0}'", this.QueueName);
                }
            }
        }

        public override string Prompt
        {
            get
            {
                if (IsInput)
                {
                    return "Enter the queue message body";
                }
                else
                {
                    return "Enter the output queue name";
                }
            }
        }

        public override string DefaultValue
        {
            get
            {
                if (IsInput)
                {
                    return null;
                }
                else
                {
                    return QueueName;
                }
            }
        }
    }
}
