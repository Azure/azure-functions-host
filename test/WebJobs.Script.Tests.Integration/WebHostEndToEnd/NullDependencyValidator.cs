// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class NullDependencyValidator : IDependencyValidator
    {
        public void Validate(IServiceCollection services)
        {            
        }
    }
}
