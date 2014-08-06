// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Blobs.Bindings
{
    internal class CancellableAsyncCompletionSource
    {
        private CompletingCancellableAsyncResult _result;

        public IAsyncResult AsyncResult
        {
            get { return _result; }
        }

        public bool Canceled
        {
            get
            {
                if (_result == null)
                {
                    throw new InvalidOperationException("SetAsyncResult was not called.");
                }

                return _result.Canceled;
            }
        }

        public void SetAsyncResult(CompletingCancellableAsyncResult result)
        {
            if (_result != null)
            {
                throw new InvalidOperationException("SetAsyncResult has already been called.");
            }

            _result = result;
        }

        public void Complete()
        {
            if (_result == null)
            {
                throw new InvalidOperationException("SetAsyncResult was not called.");
            }

            _result.Complete();
        }
    }
}
