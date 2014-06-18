using System.Collections.Generic;

namespace Dashboard.ViewModels
{
    /// <summary>Represents a page of function invocation models to send to the browser.</summary>
    public class InvocationLogSegment
    {
        public IEnumerable<InvocationLogViewModel> Entries { get; set; }
        public string ContinuationToken { get; set; }
    }
}
