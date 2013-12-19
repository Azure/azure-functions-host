using System.Collections.Generic;

namespace Microsoft.WindowsAzure.Jobs
{
    internal static class TriggerMapExtensions
    {
        public static IEnumerable<Trigger> GetTriggers(this ITriggerMap x)
        {
            foreach (var scope in x.GetScopes())
            {
                foreach (var trigger in x.GetTriggers(scope))
                {
                    yield return trigger;
                }
            }
        }
    }
}
