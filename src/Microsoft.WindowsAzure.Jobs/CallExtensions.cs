using System.Collections.Generic;

namespace Microsoft.WindowsAzure.Jobs
{
    internal static class CallExtensions
    {
        public static IFunctionToken QueueCall(this ICall call, string functionName, object arguments = null, params IFunctionToken[] prereqs)
        {
            return call.QueueCall(functionName, arguments, (IEnumerable<IFunctionToken>) prereqs);
        }
    }
}
