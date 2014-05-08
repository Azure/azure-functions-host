using System;

namespace Microsoft.Azure.Jobs
{
    // Bunder of interfaces needed for execution. Grouped together for convenience. 
    internal class ExecuteFunctionInterfaces
    {
        public IAccountInfo AccountInfo;

        public void VerifyNotNull()
        {
            if (AccountInfo == null)
            {
                throw new InvalidOperationException("AccountInfo cannot be null.");
            }
        }
    }
}
