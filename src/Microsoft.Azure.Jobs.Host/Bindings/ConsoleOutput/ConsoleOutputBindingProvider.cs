using System.IO;

namespace Microsoft.Azure.Jobs.Host.Bindings.ConsoleOutput
{
    internal class ConsoleOutputBindingProvider : IBindingProvider
    {
        public IBinding TryCreate(BindingProviderContext context)
        {
            if (context.Parameter.ParameterType != typeof(TextWriter))
            {
                return null;
            }

            return new ConsoleOutputBinding();
        }
    }
}
