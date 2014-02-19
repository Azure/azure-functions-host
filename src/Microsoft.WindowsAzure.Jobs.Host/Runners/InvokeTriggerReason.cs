using System;
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

        internal static InvokeTriggerReason Create(Guid id, string reason, Guid? parentId)
        {
            InvokeTriggerReason trigger = new InvokeTriggerReason
            {
                ChildGuid = id,
                Message = reason
            };

            if (parentId.HasValue)
            {
                trigger.ParentGuid = parentId.Value;
            }

            return trigger;
        }
    }
}
