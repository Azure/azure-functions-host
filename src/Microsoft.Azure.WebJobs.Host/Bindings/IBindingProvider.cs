// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    internal interface IBindingProvider
    {
        Task<IBinding> TryCreateAsync(BindingProviderContext context);
    }
}
