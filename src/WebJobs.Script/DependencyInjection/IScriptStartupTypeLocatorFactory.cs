// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Hosting;

namespace Microsoft.Azure.WebJobs.Script.DependencyInjection
{
    public interface IScriptStartupTypeLocatorFactory
    {
        IWebJobsStartupTypeLocator Create();
    }
}