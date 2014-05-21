namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal interface IBindingProvider
    {
        IBinding TryCreate(BindingProviderContext context);
    }
}
