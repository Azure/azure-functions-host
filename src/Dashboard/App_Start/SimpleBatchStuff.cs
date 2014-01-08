using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.WindowsAzure.Jobs;

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

        public static string LoggingConnectionStringName
        {
            get { return JobHost.LoggingConnectionStringName; }
        }
    }
}
