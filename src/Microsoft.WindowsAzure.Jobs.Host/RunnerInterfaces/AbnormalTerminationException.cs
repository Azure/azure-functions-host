using System;

namespace Microsoft.WindowsAzure.Jobs
{
    // Some error in user app (probably a stack overflow) 
    internal class AbnormalTerminationException : Exception
    {
        public AbnormalTerminationException(string msg)
            : base(msg)
        {
        }
    }
}
