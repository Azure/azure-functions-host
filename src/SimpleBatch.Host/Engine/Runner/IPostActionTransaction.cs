using System;
using SimpleBatch;

namespace RunnerHost
{
    // Bind result. Invoke a cleanup action only if the function runs successfully.
    // Invoked after all other BindResults get OnPostAction
    // This is useful for queuing a cleanup action, like deleting an input blob.
    internal interface IPostActionTransaction
    {
        void OnSuccessAction();
    }
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