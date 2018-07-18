// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Hosting;

namespace Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection
{
    /// <summary>
    /// An implementation of an <see cref="IWebJobsStartupTypeDiscoverer"/> that locates startup types
    /// from extension registrations and function extension references
    /// </summary>
    public class ScriptStartupTypeDiscoverer : IWebJobsStartupTypeDiscoverer
    {
        public Type[] GetStartupTypes()
        {
            return new Type[0];
        }
    }
}
