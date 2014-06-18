namespace Dashboard.ViewModels
{
    /// <summary>
    /// Represents a page of function invocation models to send to the browser
    /// each invocation model is accompanied by the rowkey from the index-table
    /// that pointed to it, to be used for paging.
    /// </summary>
    public class InvocationLogPage
    {
        public SortableInvocationEntry[] Entries { get; set; }
        public bool HasMore { get; set; }
        public bool IsOldHost { get; set; }
    }
}
