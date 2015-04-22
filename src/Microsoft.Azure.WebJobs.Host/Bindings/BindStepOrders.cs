// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    internal static class BindStepOrders
    {
        public static readonly int Default = 0;
        public static readonly int Enqueue = 1;
    }
}
