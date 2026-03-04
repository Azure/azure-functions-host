// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.AppCapabilities
{
    public sealed class DefaultAppCapabilitiesStore : IAppCapabilitiesStore
    {
        private readonly IOptionsChangeTokenSource<AppCapabilitiesOptions> _optionsChangeTokenSource;
        private readonly ConcurrentDictionary<string, string> _capabilities = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _updateLock = new();
        private int _isInitialized = 0;

        public DefaultAppCapabilitiesStore(IOptionsChangeTokenSource<AppCapabilitiesOptions> optionsChangeTokenSource)
        {
            _optionsChangeTokenSource = optionsChangeTokenSource ?? throw new ArgumentNullException(nameof(optionsChangeTokenSource));
        }

        public IReadOnlyDictionary<string, string> Capabilities => _capabilities;

        public bool TrySetAll(IEnumerable<KeyValuePair<string, string>> capabilities)
        {
            lock (_updateLock)
            {
                // Only allow the first worker to register capabilities as all workers tied to a JobHost instance should have the same capabilities.
                if (Interlocked.CompareExchange(ref _isInitialized, 1, 0) == 0)
                {
                    foreach (var kvp in capabilities)
                    {
                        if (kvp.Key is null || kvp.Value is null || kvp.Key == string.Empty || kvp.Value == string.Empty)
                        {
                            continue;
                        }

                        _capabilities[kvp.Key] = kvp.Value;
                    }

                    TriggerChangeNotification();
                    return true;
                }

                return false;
            }
        }

        public void Clear()
        {
            lock (_updateLock)
            {
                _capabilities.Clear();
                Interlocked.Exchange(ref _isInitialized, 0);
                TriggerChangeNotification();
            }
        }

        private void TriggerChangeNotification()
        {
            if (_optionsChangeTokenSource is AppCapabilitiesChangeTokenSource changeTokenSource)
            {
                changeTokenSource.TriggerChange();
            }
        }
    }
}