using System;

namespace Dashboard.Data
{
    internal interface IHostFunctionIndexStore
    {
        VersionedDocument<HostFunctionIndex> Read(Guid hostId);

        bool TryCreate(HostFunctionIndex index);

        bool TryUpdate(VersionedDocument<HostFunctionIndex> versionedIndex);
    }
}
