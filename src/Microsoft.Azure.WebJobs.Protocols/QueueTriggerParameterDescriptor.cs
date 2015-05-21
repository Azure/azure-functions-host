// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Globalization;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.WebJobs.Protocols
#else
namespace Microsoft.Azure.WebJobs.Host.Protocols
#endif
{
    /// <summary>Represents a parameter triggered on a queue in Azure Storage.</summary>
    [JsonTypeName("QueueTrigger")]
#if PUBLICPROTOCOL
    public class QueueTriggerParameterDescriptor : TriggerParameterDescriptor
#else
    internal class QueueTriggerParameterDescriptor : TriggerParameterDescriptor
#endif
    {
        /// <summary>Gets or sets the name of the storage account.</summary>
        public string AccountName { get; set; }

        /// <summary>Gets or sets the name of the queue.</summary>
        public string QueueName { get; set; }

        /// <inheritdoc />
        public override string GetTriggerReason(IDictionary<string, string> arguments)
        {
            return string.Format(CultureInfo.CurrentCulture, "New queue message detected on '{0}'.", QueueName);
        }
    }
}
