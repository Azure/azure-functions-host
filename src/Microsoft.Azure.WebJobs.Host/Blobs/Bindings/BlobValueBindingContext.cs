// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Bindings;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Bindings
{
    internal class BlobValueBindingContext : ValueBindingContext
    {
        public BlobValueBindingContext(BlobPath path, ValueBindingContext context)
            : base(context.FunctionContext, context.CancellationToken)
        {
            Path = path;
        }

        public BlobPath Path { get; private set; }
    }
}
