using System;
using System.Collections.Generic;

namespace Dashboard.ViewModels
{
    public class FunctionInstancesViewModel
    {
        public IEnumerable<InvocationLogViewModel> InvocationLogViewModels { get; set; }

        public string FunctionName { get; set; }
        public bool? Success { get; set; }
        public int? Count { get; set; }
        public int? Page { get; set; }
        public int? PageSize { get; set; }
    }

}