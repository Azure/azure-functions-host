using System;
using System.Collections.Generic;

namespace Microsoft.WindowsAzure.Jobs.Dashboard.Controllers
{
    public class FunctionChainModel
    {
        internal ICausalityReader Walker { get; set; }

        internal IFunctionInstanceLookup Lookup { get; set; }

        public IEnumerable<ListNode> Nodes { get; set; }

        // For computing the whole span of the chain. 
        public TimeSpan? Duration { get; set; }
    }
}
