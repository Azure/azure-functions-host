using System;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal class ArgumentBindingContext
    {
        public Guid FunctionInstanceId { get; set; }

        public INotifyNewBlob NotifyNewBlob { get; set; }
    }
}
