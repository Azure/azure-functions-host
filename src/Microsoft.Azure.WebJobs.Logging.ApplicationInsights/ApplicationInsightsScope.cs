// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Host.Loggers
{
    internal class DictionaryLoggerScope
    {
        private readonly object _state;

        private DictionaryLoggerScope(object state, DictionaryLoggerScope parent)
        {
            _state = state;
            Parent = parent;
        }

        internal DictionaryLoggerScope Parent { get; private set; }

        private static AsyncLocal<DictionaryLoggerScope> _value = new AsyncLocal<DictionaryLoggerScope>();

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
                foreach (var entry in current.GetStateDictionary())
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

        private IDictionary<string, object> GetStateDictionary() => _state as IDictionary<string, object>;

        private class DisposableScope : IDisposable
        {
            public void Dispose()
            {
                Current = Current.Parent;
            }
        }
    }
}