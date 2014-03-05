using System;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class FunctionInJobEntity : TableServiceEntity
    {
        public Guid InvocationId { get; set; }
    }
}