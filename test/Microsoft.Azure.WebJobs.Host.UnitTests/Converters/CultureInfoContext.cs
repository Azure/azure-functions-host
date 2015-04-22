// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Converters
{
    internal sealed class CultureInfoContext : IDisposable
    {
        private readonly CultureInfo _previousCurrentCulture;
        private readonly CultureInfo _previousCurrentUICulture;

        private bool _disposed;

        public CultureInfoContext(CultureInfo cultureInfo)
        {
            _previousCurrentCulture = Thread.CurrentThread.CurrentCulture;
            _previousCurrentUICulture = Thread.CurrentThread.CurrentUICulture;
            Thread.CurrentThread.CurrentCulture = cultureInfo;
            Thread.CurrentThread.CurrentUICulture = cultureInfo;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Thread.CurrentThread.CurrentCulture = _previousCurrentCulture;
                Thread.CurrentThread.CurrentUICulture = _previousCurrentUICulture;

                _disposed = true;
            }
        }
    }
}
