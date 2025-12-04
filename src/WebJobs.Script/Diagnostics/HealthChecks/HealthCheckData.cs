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

        /// <summary>
        /// Gets or sets the area of the health check data failure.
        /// </summary>
        /// <remarks>
        /// This is the area that has failed. Such as "configuration", "connectivity", etc.
        /// </remarks>
        public string Area
        {
            get => GetOrDefault<string>();
            set => Set(value);
        }

        /// <summary>
        /// Gets or sets the configuration section related to the health check data.
        /// </summary>
        /// <remarks>
        /// Useful for when the component being checked is related to a specific configuration section.
        /// </remarks>
        public string ConfigurationSection
        {
            get => GetOrDefault<string>();
            set => Set(value);
        }

        /// <summary>
        /// Gets or sets the status code related to the health check data.
        /// For HTTP related related checks, this is the HTTP status code.
        /// </summary>
        public int StatusCode
        {
            get => GetOrDefault<int>();
            set => Set(value);
        }

        /// <summary>
        /// Gets or sets the error code related to the health check data.
        /// </summary>
        /// <remarks>
        /// For Azure SDK related checks, this is typically the RequestFailedException.ErrorCode value.
        /// </remarks>
        public string ErrorCode
        {
            get => GetOrDefault<string>();
            set => Set(value);
        }

        /// <summary>
        /// Sets exception details into the health check data.
        /// </summary>
        /// <param name="ex">The exception to set details from.</param>
        /// <remarks>
        /// This will set various properties based on the type of exception.
        /// </remarks>
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
