using System;
using System.Collections.Generic;

namespace Dashboard.Data
{
    public interface IVersionMetadataMapper
    {
        DateTimeOffset GetVersion(IDictionary<string, string> metadata);

        void SetVersion(DateTimeOffset version, IDictionary<string, string> metadata);
    }
}
