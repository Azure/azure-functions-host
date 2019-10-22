// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    internal class SpecializationChangeTokenSource<TOptions> : IOptionsChangeTokenSource<TOptions>
    {
        private readonly IOptionsChangeTokenSource<StandbyOptions> _standby;

        public SpecializationChangeTokenSource(IOptionsChangeTokenSource<StandbyOptions> standby)
        {
            // When standby occurs, we also want compat options to re-evaluate.
            _standby = standby;
        }

        public string Name { get; }

        public IChangeToken GetChangeToken()
        {
            return _standby.GetChangeToken();
        }
    }
}