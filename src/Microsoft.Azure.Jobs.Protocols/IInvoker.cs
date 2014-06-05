using System;

namespace Microsoft.Azure.Jobs.Protocols
{
    /// <summary>Defines a host function invoker.</summary>
    public interface IInvoker
    {
        /// <summary>Triggers a function using override values for all parameters.</summary>
        /// <param name="queueName">The name of the queue to which the host is listening.</param>
        /// <param name="message">The message containing data about the function to trigger.</param>
        void TriggerAndOverride(string queueName, TriggerAndOverrideMessage message);
    }
}
