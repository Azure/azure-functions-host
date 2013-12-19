using System;

namespace Microsoft.WindowsAzure.Jobs
{
    internal interface IFunctionToken
    {
        Guid Guid { get; }
    }
}
