using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    interface IBindableTableEntityPath : IBindablePath<TableEntityPath>
    {
        string TableNamePattern { get; }
        string PartitionKeyPattern { get; }
        string RowKeyPattern { get; }
    }
}
