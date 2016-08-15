// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Web.Http.Dependencies;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public static class DependencyResolverExtensions
    {
        public static TService GetService<TService>(this IDependencyResolver resolver)
        {
            return (TService)resolver.GetService(typeof(TService));
        }
    }
}