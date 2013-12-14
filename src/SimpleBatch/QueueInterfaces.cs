using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Microsoft.WindowsAzure.Jobs
{
    // Allow for queuing multiple output parameters
    public interface IQueueOutput<T>
    {
        void Add(T payload);

        // Delay: see http://msdn.microsoft.com/en-us/library/windowsazure/hh563575.aspx 
        void Add(T payload, TimeSpan delay);
    }
}
