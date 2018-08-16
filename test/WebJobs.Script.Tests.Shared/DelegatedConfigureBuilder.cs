// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class DelegatedConfigureBuilder<TBuilder> : IConfigureBuilder<TBuilder>
    {
        private readonly Action<TBuilder> _builder;

        public DelegatedConfigureBuilder(Action<TBuilder> builder)
        {
            _builder = builder;
        }

        public void Configure(TBuilder builder)
        {
            _builder?.Invoke(builder);
        }
    }
}
