using System.Runtime.InteropServices;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public static class TestStringExtensions
    {
        public static string ToPlatformPath(this string path)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return path.Replace("/", "\\");
            }
            else
            {
                if (path.IndexOf(":") != -1)
                {
                    path = path.Substring(2);
                }
                return path.Replace("\\", "/");
            }
        }
    }
}
