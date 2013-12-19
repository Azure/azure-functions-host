namespace Microsoft.WindowsAzure.Jobs
{
    internal class TimerTriggerReason : TriggerReason
    {
        public override string ToString()
        {
            return "Timer fired";
        }
    }
}
