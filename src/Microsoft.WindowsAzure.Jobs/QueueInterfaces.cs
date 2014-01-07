using System;

namespace Microsoft.WindowsAzure.Jobs
{
    // Allow for queuing multiple output parameters
    internal interface IQueueOutput<T>
    {
        void Add(T payload);

        // Delay: see http://msdn.microsoft.com/en-us/library/windowsazure/hh563575.aspx 
        void Add(T payload, TimeSpan delay);
    }
}
