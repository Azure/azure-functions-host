using System.Collections.Generic;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal class BindingContext
    {
        public IReadOnlyDictionary<string, object> BindingData { get; set; }
    }
}
