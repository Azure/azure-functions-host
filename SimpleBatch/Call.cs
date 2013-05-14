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

    public static class ICallExtensions
    {
        // Queues a call to the given function. Function is resolved against the current "scope". 
        // arguments can be either an IDictionary or an anonymous object with fields.         
        public static IFunctionToken QueueCall(this ICall call, string functionName, object arguments = null, params IFunctionToken[] prereqs)
        {
            return call.QueueCall(functionName, arguments, (IEnumerable<IFunctionToken>) prereqs);
        }
    }

    public interface IFunctionToken
    {
        Guid Guid { get; }
    }
}