// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    internal static class TestServiceCollectionExtensions
    {
        public static IServiceCollection SkipDependencyValidation(this IServiceCollection services)
        {
            return services.Replace(new ServiceDescriptor(typeof(IDependencyValidator), new TestDependencyValidator()));
        }

        private class TestDependencyValidator : IDependencyValidator
        {
            public void Validate(IServiceCollection services)
            {
                // no-op for tests; this allows us to override anything
            }
        }
    }
}
