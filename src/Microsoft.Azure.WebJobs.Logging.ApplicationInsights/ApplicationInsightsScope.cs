// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Logging.ApplicationInsights
{
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

        public static IDisposable Push(IReadOnlyDictionary<string, object> state)
        {
            Current = new DictionaryLoggerScope(state, Current);
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