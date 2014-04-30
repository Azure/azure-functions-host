using Microsoft.Azure.Jobs;

namespace Dashboard.ViewModels
{
    /// <summary>
    /// Attaches the Key in the index that used to retrive an invocation as
    /// part of a query.
    /// For example when querying for invocations in a JobRun using the <see cref="FunctionInvocationIndexReader"/>.
    /// </summary>
    public class SortableInvocationEntry
    {
        public string Key { get; set; }
        public InvocationLogViewModel Invocation { get; set; }
    }

    /// <summary>
    /// Represents a page of function invocation models to send to the browser
    /// each invocation model is accompanied by the rowkey from the index-table
    /// that pointed to it, to be used for paging.
    /// </summary>
    public class InvocationLogPage
    {
        public SortableInvocationEntry[] Entries { get; set; }
        public bool HasMore { get; set; }
    }
}