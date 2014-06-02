namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal class RuntimeBindingProvider : IBindingProvider
    {
        public IBinding TryCreate(BindingProviderContext context)
        {
            if (context.Parameter.ParameterType != typeof(IBinder))
            {
                return null;
            }

            return new RuntimeBinding();
        }
    }
}
