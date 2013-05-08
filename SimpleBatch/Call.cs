using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using SimpleBatch;

namespace SimpleBatch
{
    public interface ICall
    {
        // Queues a call to the given function. Function is resolved against the current "scope". 
        // arguments can be either an IDictionary or an anonymous object with fields.         
        IFunctionToken QueueCall(string functionName, object arguments = null, IEnumerable<IFunctionToken> prereqs = null);
    }

    public interface IFunctionToken
    {
    }
}