// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Hosting;
using TestProject_Razor;

[assembly: WebJobsStartup(typeof(Startup))]

namespace TestProject_Razor
{
    public class Startup : IWebJobsStartup
    {
        public void Configure(IWebJobsBuilder builder)
        {
        }
    }
}