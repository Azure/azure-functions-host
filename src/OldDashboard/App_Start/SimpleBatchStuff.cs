using System;
using System.Collections.Generic;
using System.Reflection;

namespace Dashboard
{
    public class SimpleBatchStuff
    {
        public static string BadInitErrorMessage;
        public static bool BadInit
        {
            get { return BadInitErrorMessage != null; }
        }

        internal static IEnumerable<Assembly> GetUserAssemblies()
        {
            return AppDomain.CurrentDomain.GetAssemblies();
        }
    }
}
