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
                throw new InvalidOperationException("Logger cannot be null.");
            }
            if (Lookup == null)
            {
                throw new InvalidOperationException("Lookup cannot be null.");
            }
            if (AccountInfo == null)
            {
                throw new InvalidOperationException("AccountInfo cannot be null.");
            }
            if (CausalityLogger == null)
            {
                throw new InvalidOperationException("CausalityLogger cannot be null.");
            }
            if (PrereqManager == null)
            {
                throw new InvalidOperationException("PrereqManager cannot be null.");
            }
        }
    }
}
