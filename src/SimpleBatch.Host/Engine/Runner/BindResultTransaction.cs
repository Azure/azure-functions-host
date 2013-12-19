using System;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class BindResultTransaction : BindResult, IPostActionTransaction
    {
        public Action Cleanup;

        public void OnSuccessAction()
        {
            if (Cleanup != null)
            {
                Cleanup();
            }
        }
    }
}
