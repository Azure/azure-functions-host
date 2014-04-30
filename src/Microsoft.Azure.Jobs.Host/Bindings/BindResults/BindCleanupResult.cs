using System;

namespace Microsoft.Azure.Jobs
{
    // Helper to include a cleanup function with bind result
    internal class BindCleanupResult : BindResult
    {
        public Action Cleanup;
        public ISelfWatch SelfWatch;

        public override ISelfWatch Watcher
        {
            get
            {
                return this.SelfWatch ?? base.Watcher;
            }
        }

        public override void OnPostAction()
        {
            if (Cleanup != null)
            {
                Cleanup();
            }
        }
    }
}
