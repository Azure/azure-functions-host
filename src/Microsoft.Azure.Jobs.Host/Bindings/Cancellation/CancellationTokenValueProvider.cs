// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Host.Bindings.Cancellation
{
    internal class CancellationTokenValueProvider : IValueProvider
    {
        private readonly CancellationToken _token;

        public CancellationTokenValueProvider(CancellationToken token)
        {
            _token = token;
        }

        public Type Type
        {
            get { return typeof(CancellationToken); }
        }

        public object GetValue()
        {
            return _token;
        }

        public string ToInvokeString()
        {
            return null;
        }
    }
}
