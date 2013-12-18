using System;
using System.Collections.Generic;

namespace Microsoft.WindowsAzure.Jobs
{
    internal interface ICall
    {
        // Queues a call to the given function. Function is resolved against the current "scope". 
        // arguments can be either an IDictionary or an anonymous object with fields.         
        IFunctionToken QueueCall(string functionName, object arguments = null, IEnumerable<IFunctionToken> prereqs = null);
    }

    internal static class ICallExtensions
    {
        public static IFunctionToken QueueCall(this ICall call, string functionName, object arguments = null, params IFunctionToken[] prereqs)
        {
            return call.QueueCall(functionName, arguments, (IEnumerable<IFunctionToken>) prereqs);
        }
    }

    internal interface IFunctionToken
    {
        Guid Guid { get; }
    }
}
