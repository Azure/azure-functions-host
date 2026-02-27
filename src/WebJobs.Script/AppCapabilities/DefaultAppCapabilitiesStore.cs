// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.AppCapabilities
{
    public sealed class DefaultAppCapabilitiesStore : IAppCapabilitiesStore
    {
        private readonly IOptionsChangeTokenSource<AppCapabilitiesOptions> _optionsChangeTokenSource;
        private readonly ConcurrentDictionary<string, string> _capabilities = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _updateLock = new();

        public DefaultAppCapabilitiesStore(IOptionsChangeTokenSource<AppCapabilitiesOptions> optionsChangeTokenSource)
        {
            _optionsChangeTokenSource = optionsChangeTokenSource ?? throw new ArgumentNullException(nameof(optionsChangeTokenSource));
        }

        public IReadOnlyDictionary<string, string> Capabilities => _capabilities;

        public void SetAll(IEnumerable<KeyValuePair<string, string>> capabilities)
        {
            lock (_updateLock)
            {
                foreach (var kvp in capabilities)
                {
                    if (!string.IsNullOrEmpty(kvp.Key) && !string.IsNullOrEmpty(kvp.Value))
                    {
                        _capabilities[kvp.Key] = kvp.Value;
                    }
                }

                TriggerChangeNotification();
            }
        }

        public void Clear()
        {
            lock (_updateLock)
            {
                _capabilities.Clear();
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
