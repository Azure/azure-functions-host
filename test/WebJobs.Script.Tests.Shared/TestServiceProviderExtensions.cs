// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.DependencyInjection;

namespace System
{
    public static class TestServiceProviderExtensions
    {
        public static IDisposable CreateScopedEnvironment(this IServiceProvider services, IDictionary<string, string> scopedVariables)
        {
            var environment = services.GetService<IEnvironment>();

            if (environment == null)
            {
                throw new InvalidOperationException("The specified host does not have an IEnvironment service.");
            }

            return new ScopedEnvironment(environment, scopedVariables);
        }

        private class ScopedEnvironment : IDisposable
        {
            private readonly IDictionary<string, string> _originalValues;
            private readonly IEnvironment _environment;

            public ScopedEnvironment(IEnvironment environment, IDictionary<string, string> scopedVariables)
            {
                _originalValues = new Dictionary<string, string>(scopedVariables.Count);
                _environment = environment;

                foreach (var variable in scopedVariables)
                {
                    _originalValues.Add(variable.Key, environment.GetEnvironmentVariable(variable.Key));
                    _environment.SetEnvironmentVariable(variable.Key, variable.Value);
                }
            }

            public void Dispose()
            {
                foreach (var variable in _originalValues)
                {
                    _environment.SetEnvironmentVariable(variable.Key, variable.Value);
                }
            }
        }
    }
}
