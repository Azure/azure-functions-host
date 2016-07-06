using System;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Kudu
{
    public static class StringExtensions
    {
        public static string EscapeHashCharacter(this string str)
        {
            return str.Replace("#", Uri.EscapeDataString("#"));
        }
    }
}
