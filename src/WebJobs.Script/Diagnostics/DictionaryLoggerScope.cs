// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    /// <summary>
    /// This class is identical to the internal class in WebJobs.Host. It is copied here so we can
    /// keep it internal.
    /// </summary>
    internal class DictionaryLoggerScope
    {
        private static AsyncLocal<DictionaryLoggerScope> _value = new AsyncLocal<DictionaryLoggerScope>();

        private DictionaryLoggerScope(IReadOnlyDictionary<string, object> state, DictionaryLoggerScope parent)
        {
            State = state;
            Parent = parent;
        }

        internal IReadOnlyDictionary<string, object> State { get; private set; }

        internal DictionaryLoggerScope Parent { get; private set; }

        public static DictionaryLoggerScope Current
        {
            get
            {
                return _value.Value;
            }

            set
            {
                _value.Value = value;
            }
        }

        public static IDisposable Push(object state)
        {
            IDictionary<string, object> stateValues;

            if (state is IEnumerable<KeyValuePair<string, object>> stateEnum)
            {
                // Convert this to a dictionary as we have scenarios where we cannot have duplicates. In this
                // case, if there are dupes, the later entry wins.
                stateValues = new Dictionary<string, object>();
                foreach (var entry in stateEnum)
                {
                    stateValues[entry.Key] = entry.Value;
                }
            }
            else
            {
                // There's nothing we can do with other states.
                return null;
            }

            Current = new DictionaryLoggerScope(new ReadOnlyDictionary<string, object>(stateValues), Current);
            return new DisposableScope();
        }

        // Builds a state dictionary of all scopes. If an inner scope
        // contains the same key as an outer scope, it overwrites the value.
        public static IDictionary<string, object> GetMergedStateDictionary()
        {
            IDictionary<string, object> scopeInfo = new Dictionary<string, object>();

            var current = Current;
            while (current != null)
            {
                foreach (var entry in current.State)
                {
                    // inner scopes win
                    if (!scopeInfo.Keys.Contains(entry.Key))
                    {
                        scopeInfo.Add(entry);
                    }
                }
                current = current.Parent;
            }

            return scopeInfo;
        }

        private class DisposableScope : IDisposable
        {
            public void Dispose()
            {
                Current = Current.Parent;
            }
        }
    }
}