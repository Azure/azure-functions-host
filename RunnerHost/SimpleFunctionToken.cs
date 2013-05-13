using System;
using System.Collections.Generic;
using SimpleBatch;
using SimpleBatch.Client;
using System.Linq;

namespace RunnerHost
{
    public class SimpleFunctionToken : IFunctionToken
    {
        public SimpleFunctionToken(Guid g)
        {
            this.Guid = g;
        }

        public Guid Guid { get; private set; }
    }
}