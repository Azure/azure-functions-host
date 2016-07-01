using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WebJobs.Script.ConsoleHost.Arm.Extensions
{
    public static class Extensions
    {
        public static string NullStatus(this object o)
        {
            return o == null ? "Null" : "NotNull";
        }
    }
}