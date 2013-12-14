using System;
using System.Collections.Generic;

using System.Linq;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class CallUtil
    {
        public static IEnumerable<Guid> Unwrap(IEnumerable<IFunctionToken> prereqs)
        {
            if (prereqs == null)
            {
                return null;
            }
            var prereqs2 = from prereq in prereqs select prereq.Guid;
            return prereqs2;
        }

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
