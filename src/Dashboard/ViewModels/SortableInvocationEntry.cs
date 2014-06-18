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
}
