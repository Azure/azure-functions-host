using Microsoft.ServiceBus;

namespace Microsoft.Azure.WebJobs.Script.Binding.ServiceBus
{
    public class AccessRightsValidator
    {
        private readonly NamespaceManager _manager;

        public AccessRightsValidator(NamespaceManager manager)
        {
            _manager = manager;
        }

        public virtual void TestManageRights()
        {
            _manager.GetQueues();
        }
    }
}
