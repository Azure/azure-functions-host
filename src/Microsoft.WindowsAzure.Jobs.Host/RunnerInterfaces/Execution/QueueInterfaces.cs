using System;

namespace Microsoft.WindowsAzure.Jobs
{
    // Bunder of interfaces needed for execution. Grouped together for convenience. 
    internal class QueueInterfaces
    {
        public IAccountInfo AccountInfo;
        public IFunctionInstanceLookup Lookup;
        public IFunctionUpdatedLogger Logger;
        public ICausalityLogger CausalityLogger;
        public IPrereqManager PrereqManager;

        public void VerifyNotNull()
        {
            if (Logger == null)
            {
                throw new ArgumentNullException("Logger");
            }
            if (Lookup == null)
            {
                throw new ArgumentNullException("Lookup");
            }
            if (AccountInfo == null)
            {
                throw new ArgumentNullException("AccountInfo");
            }
            if (CausalityLogger == null)
            {
                throw new ArgumentNullException("CausalityLogger");
            }
            if (PrereqManager == null)
            {
                throw new ArgumentNullException("PrereqManager");
            }
        }
    }
}
