// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    internal enum GrpcCapabilitiesUpdateStrategy
    {
        // overwrites existing values and appends new ones
        // ex. worker init: {A: foo, B: bar} + env reload: {A:foo, B: foo, C: foo} -> {A: foo, B: foo, C: foo}
        Merge,
        // existing capabilities are cleared and new capabilities are applied
        // ex. worker init: {A: foo, B: bar} + env reload: {A:foo, C: foo} -> {A: foo, C: foo}
        Replace
    }
}
