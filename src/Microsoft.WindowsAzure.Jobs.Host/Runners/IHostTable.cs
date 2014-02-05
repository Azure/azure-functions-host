using System;

namespace Microsoft.WindowsAzure.Jobs
{
    internal interface IHostTable
    {
        Guid GetOrCreateHostId(string hostName);
    }
}
