// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Tests.Middleware
{
    public class TestHostEasyAuthOptionsSetup : IConfigureOptions<HostEasyAuthOptions>
    {
        private readonly HostEasyAuthOptions _options;

        public TestHostEasyAuthOptionsSetup(HostEasyAuthOptions options)
        {
            _options = options;
        }

        public void Configure(HostEasyAuthOptions options)
        {
            options.SiteAuthClientId = _options.SiteAuthClientId;
            options.SiteAuthEnabled = _options.SiteAuthEnabled;
        }
    }
}
