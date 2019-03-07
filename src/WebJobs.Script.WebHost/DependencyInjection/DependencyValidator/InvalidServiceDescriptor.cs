// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection
{
    internal class InvalidServiceDescriptor
    {
        public InvalidServiceDescriptor(ServiceDescriptor descriptor, InvalidServiceDescriptorReason reason)
        {
            Descriptor = descriptor;
            Reason = reason;
        }

        public InvalidServiceDescriptorReason Reason { get; private set; }

        public ServiceDescriptor Descriptor { get; private set; }
    }
}
