using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Azure.Jobs
{
    internal class CallUtil
    {
        // Pass parent guid through parameters as special keyname.
        const string FunctionInstanceGuidKeyName = "$this";

        public static void AddFunctionGuid(Guid thisFunc, IDictionary<string, string> args)
        {
            args[FunctionInstanceGuidKeyName] = thisFunc.ToString();
        }

        public static Guid GetParentGuid(IDictionary<string, string> args)
        {
            if (args != null)
            {
                string guidAsString;
                if (args.TryGetValue(FunctionInstanceGuidKeyName, out guidAsString))
                {
                    return Guid.Parse(guidAsString);
                }
            }
            return Guid.Empty;
        }
    }
}
