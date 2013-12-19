using System;
using System.IO;

namespace Microsoft.WindowsAzure.Jobs
{
    // Helper to redirect std.out if this function is launched as an appdomain.
    // This a hook that can be invoked by whoever creates the appdomain.
    // See:
    // http://blogs.artinsoft.net/mrojas/archive/2008/10/02/outofprocess-in-c.aspx
    internal class OutputSetter : MarshalByRefObject
    {
        public OutputSetter()
        {
        }
        public void SetOut(TextWriter output)
        {
            Console.SetOut(output);
        }
    }
}
