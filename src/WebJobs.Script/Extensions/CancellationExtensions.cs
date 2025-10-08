// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class CancellationExtensions
    {
        /// <summary>
        /// Cancels the <see cref="CancellationTokenSource"/>, ignoring any <see cref="ObjectDisposedException"/>.
        /// </summary>
        /// <param name="cts">The <see cref="CancellationTokenSource"/> to cancel.</param>
        public static void CancelNoThrow(this CancellationTokenSource cts)
        {
            ArgumentNullException.ThrowIfNull(cts);

            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Ignore disposed exceptions
            }
        }

        /// <summary>
        /// Cancels the <see cref="CancellationTokenSource"/>, ignoring any <see cref="ObjectDisposedException"/>.
        /// </summary>
        /// <param name="cts">The <see cref="CancellationTokenSource"/> to cancel.</param>
        /// <returns>A task that represents the asynchronous cancel operation.</returns>
        public static async Task CancelNoThrowAsync(this CancellationTokenSource cts)
        {
            ArgumentNullException.ThrowIfNull(cts);

            try
            {
                await cts.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
                // Ignore disposed exceptions
            }
        }
    }
}
