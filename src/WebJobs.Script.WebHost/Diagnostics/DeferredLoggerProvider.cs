// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal sealed class DeferredLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly DeferredLogSource _source;
        private readonly IEnvironment _environment;
        private IExternalScopeProvider _scopeProvider;

        public DeferredLoggerProvider(DeferredLogSource source, IEnvironment environment)
        {
            _source = source;
            _environment = environment;
        }

        public int Count => _source.Reader.Count;

        public ILogger CreateLogger(string categoryName)
        {
            return _source.IsEnabled ? new DeferredLogger(_source, categoryName, _scopeProvider, _environment) : NullLogger.Instance;
        }

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
        }

        public void Dispose()
        {
            _source.Disable();
        }
    }
}
