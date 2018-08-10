// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class DelegatedScriptJobHostBuilder : IConfigureWebJobsBuilder
    {
        private readonly Action<IWebJobsBuilder> _builder;

        public DelegatedScriptJobHostBuilder(Action<IWebJobsBuilder> builder)
        {
            _builder = builder;
        }

        public void Configure(IWebJobsBuilder builder)
        {
            _builder(builder);
        }
    }
}
