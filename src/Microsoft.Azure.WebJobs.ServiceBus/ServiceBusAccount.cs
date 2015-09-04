// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Reflection;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    internal class ServiceBusAccount
    {
        public MessagingFactory MessagingFactory { get; set; }

        public NamespaceManager NamespaceManager { get; set; }

        internal static string GetAccountOverrideOrNull(ParameterInfo parameter)
        {
            ServiceBusAccountAttribute attribute = parameter.GetCustomAttribute<ServiceBusAccountAttribute>();
            if (attribute != null)
            {
                return attribute.Account;
            }

            attribute = parameter.Member.GetCustomAttribute<ServiceBusAccountAttribute>();
            if (attribute != null)
            {
                return attribute.Account;
            }

            attribute = parameter.Member.DeclaringType.GetCustomAttribute<ServiceBusAccountAttribute>();
            if (attribute != null)
            {
                return attribute.Account;
            }

            return null;
        }
    }
}
