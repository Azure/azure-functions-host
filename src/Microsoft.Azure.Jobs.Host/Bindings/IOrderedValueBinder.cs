namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal interface IOrderedValueBinder : IValueBinder
    {
        int StepOrder { get; }
    }
}
