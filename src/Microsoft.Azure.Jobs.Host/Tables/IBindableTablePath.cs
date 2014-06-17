using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    interface IBindableTablePath : IBindablePath<string>
    {
        string TableNamePattern { get; }
    }
}
