using System;

using System.Diagnostics;

namespace Microsoft.WindowsAzure.Jobs
{
    [DebuggerDisplay("{Guid}")]
    internal class SimpleFunctionToken : IFunctionToken
    {
        public SimpleFunctionToken(Guid g)
        {
            this.Guid = g;
        }

        public Guid Guid { get; private set; }
    }
}