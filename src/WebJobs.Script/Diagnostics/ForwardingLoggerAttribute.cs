// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Logging
{
    [AttributeUsage(AttributeTargets.Parameter)]
    internal class ForwardingLoggerAttribute()
        : FromKeyedServicesAttribute(ForwardingLogger.ServiceKey)
    {
    }
}
