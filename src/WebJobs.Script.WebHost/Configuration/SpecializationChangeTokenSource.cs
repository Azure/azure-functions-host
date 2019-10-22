// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    // An implementation of IOptionsChangeTokenSource<TOptions> that automatically signals its change token when specialization occurs.
    internal class SpecializationChangeTokenSource<TOptions> : IOptionsChangeTokenSource<TOptions>
    {
        private readonly IOptionsChangeTokenSource<StandbyOptions> _standbyChangeTokenSource;

        public SpecializationChangeTokenSource(IOptionsChangeTokenSource<StandbyOptions> standbyChangeTokenSource)
        {
            // When standby occurs, we also want compat options to re-evaluate.
            _standbyChangeTokenSource = standbyChangeTokenSource;
        }

        public string Name { get; }

        public IChangeToken GetChangeToken()
        {
            return _standbyChangeTokenSource.GetChangeToken();
        }
    }
}