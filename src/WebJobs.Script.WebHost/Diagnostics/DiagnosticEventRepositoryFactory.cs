// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class DiagnosticEventRepositoryFactory : IDiagnosticEventRepositoryFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public DiagnosticEventRepositoryFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public IDiagnosticEventRepository Create()
        {
            // Using this to break ciruclar dependency for ILoggers. Typically you cannot log errors within the logging pipeline because it creates infinte loop.
            // However in this case that loop is broken because of the filtering in the DiagnosticEventLogger
            return _serviceProvider.GetRequiredService<IDiagnosticEventRepository>();
        }
    }
}