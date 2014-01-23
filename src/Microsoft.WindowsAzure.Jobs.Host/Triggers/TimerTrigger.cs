using System;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class TimerTrigger : Trigger
    {
        public TimerTrigger()
        {
            this.Type = TriggerType.Timer;
        }
        public TimeSpan Interval { get; set; }

        public override string ToString()
        {
            return string.Format("Trigger on {0} interval", Interval);
        }
    }
}
