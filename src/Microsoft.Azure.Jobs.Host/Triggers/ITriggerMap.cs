using System.Collections.Generic;

namespace Microsoft.Azure.Jobs
{
    internal interface ITriggerMap
    {
        // Scope can be a user's site. 
        Trigger[] GetTriggers(string scope);

        void AddTriggers(string scope, params Trigger[] triggers);

        IEnumerable<string> GetScopes();
    }
}
