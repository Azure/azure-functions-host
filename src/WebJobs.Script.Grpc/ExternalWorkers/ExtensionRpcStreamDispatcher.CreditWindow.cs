// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

internal sealed partial class ExtensionRpcStreamDispatcher
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CreditWindow"/> class.
    /// </summary>
    /// <param name="initialCredits">The number of bytes initially available.</param>
    private sealed class CreditWindow(ulong initialCredits)
    {
        private readonly Lock _syncLock = new();
        private ulong _available = initialCredits;
        private TaskCompletionSource _changed = CreateChangedSource();

        /// <summary>
        /// Adds byte credits granted by the proxy.
        /// </summary>
        /// <param name="credits">The number of credits to add.</param>
        public void Add(ulong credits)
        {
            if (credits is 0)
            {
                return;
            }

            TaskCompletionSource changed;
            lock (_syncLock)
            {
                _available = ulong.MaxValue - _available < credits ? ulong.MaxValue : _available + credits;
                changed = _changed;
                _changed = CreateChangedSource();
            }

            changed.TrySetResult();
        }

        /// <summary>
        /// Waits for and reserves the requested number of response byte credits.
        /// </summary>
        /// <param name="credits">The number of credits to reserve.</param>
        /// <param name="cancellationToken">A token that cancels the wait.</param>
        /// <returns>A task that completes when the credits have been reserved.</returns>
        public async ValueTask ReserveAsync(ulong credits, CancellationToken cancellationToken)
        {
            if (credits is 0)
            {
                return;
            }

            while (true)
            {
                Task waitTask;
                lock (_syncLock)
                {
                    if (_available >= credits)
                    {
                        _available -= credits;

                        return;
                    }

                    waitTask = _changed.Task;
                }

                await waitTask.WaitAsync(cancellationToken);
            }
        }

        private static TaskCompletionSource CreateChangedSource() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
