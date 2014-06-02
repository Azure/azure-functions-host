using System.Collections.Generic;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal class BindingContext : ArgumentBindingContext
    {
        public IReadOnlyDictionary<string, object> BindingData { get; set; }

        public IBindingProvider BindingProvider { get; set; }
    }
}
