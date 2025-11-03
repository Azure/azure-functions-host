// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Azure;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.HealthChecks
{
    /// <summary>
    /// A helper for providing data with a health check result.
    /// </summary>
    internal partial class HealthCheckData
    {
        // exposed to the HealthCheckResult through IReadOnlyDictionary.
        private readonly Dictionary<string, object> _data = [];

        public string Source
        {
            get => GetOrDefault<string>();
            set => Set(value);
        }

        public string ConfigurationSection
        {
            get => GetOrDefault<string>();
            set => Set(value);
        }

        public int StatusCode
        {
            get => GetOrDefault<int>();
            set => Set(value);
        }

        public string ErrorCode
        {
            get => GetOrDefault<string>();
            set => Set(value);
        }

        public void SetExceptionDetails(Exception ex)
        {
            ArgumentNullException.ThrowIfNull(ex);
            if (ex is AggregateException aggregate)
            {
                // Azure SDK will retry a few times in some cases, leading to multiple inner exceptions.
                // We only care about the last one.
                ex = aggregate.InnerExceptions.Last();
            }

            if (ex is TimeoutException)
            {
                ErrorCode = "Timeout";
            }
            else if (ex is OperationCanceledException)
            {
                ErrorCode = "OperationCanceled";
            }
            else if (ex is RequestFailedException rfe)
            {
                StatusCode = rfe.Status;
                ErrorCode = rfe.ErrorCode;
            }
        }

        private void Set<T>(T value, [CallerMemberName] string key = null)
        {
            _data[key] = value;
        }

        private T GetOrDefault<T>([CallerMemberName] string key = null, T defaultValue = default)
        {
            if (_data.TryGetValue(key, out var value) && value is T typedValue)
            {
                return typedValue;
            }

            return defaultValue;
        }
    }

    // Partial class down here to separate IReadOnlyDictionary implementation details.
    internal partial class HealthCheckData : IReadOnlyDictionary<string, object>
    {
        IEnumerable<string> IReadOnlyDictionary<string, object>.Keys
            => _data.Keys;

        IEnumerable<object> IReadOnlyDictionary<string, object>.Values
            => _data.Values;

        int IReadOnlyCollection<KeyValuePair<string, object>>.Count
            => _data.Count;

        object IReadOnlyDictionary<string, object>.this[string key]
            => _data[key];

        bool IReadOnlyDictionary<string, object>.ContainsKey(string key)
            => _data.ContainsKey(key);

        IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator()
            => _data.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => _data.GetEnumerator();

        bool IReadOnlyDictionary<string, object>.TryGetValue(string key, out object value)
            => _data.TryGetValue(key, out value);
    }
}
