using System;
using System.Collections.Generic;
using SimpleBatch;
using SimpleBatch.Client;
using System.Linq;
using System.Diagnostics;

namespace RunnerHost
{
    [DebuggerDisplay("{Guid}")]
    public class SimpleFunctionToken : IFunctionToken
    {
        public SimpleFunctionToken(Guid g)
        {
            this.Guid = g;
        }

        public Guid Guid { get; private set; }
    }
}