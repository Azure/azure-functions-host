using System;
using System.Collections.Generic;

namespace Dashboard.ViewModels
{
    public class FunctionInstanceDetailsViewModel
    {
        public InvocationLogViewModel InvocationLogViewModel { get; set; }

        public ParamModel[] Parameters { get; set; }

        public IEnumerable<Guid> ChildrenIds { get; set; }

        public InvocationLogViewModel Ancestor { get; set; }

        public TriggerReasonViewModel TriggerReason { get; set; }

        public bool IsAborted { get; set; }
    }
}
