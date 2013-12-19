using System;

namespace Microsoft.WindowsAzure.Jobs
{
    // Activate a function that was attempted to be queued, but didn't yet have prereqs. 
    internal interface IActivateFunction
    {
        void ActivateFunction(Guid instance);
    }
}
