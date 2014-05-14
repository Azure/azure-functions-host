using System;

namespace Microsoft.Azure.Jobs.Protocols
{
    /// <summary>Defines a host function invoker.</summary>
    public interface IInvoker
    {
        /// <summary>Triggers a function using override values for all parameters.</summary>
        /// <param name="hostId">The ID of the host.</param>
        /// <param name="message">The message containing data about the function to trigger.</param>
        void TriggerAndOverride(Guid hostId, TriggerAndOverrideMessage message);
    }
}
