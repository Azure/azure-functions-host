// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    public class ScriptTypeLocator : ITypeLocator
    {
        private Type[] _types;
        private static readonly ConcurrentDictionary<string, List<DateTime>> _methodsCalled = new();

        public ScriptTypeLocator()
        {
            _types = Array.Empty<Type>();
        }

        public ConcurrentDictionary<string, List<DateTime>> GetMethodsCalled() => _methodsCalled;

        public IReadOnlyList<Type> GetTypes()
        {
            LogCallHistory(nameof(GetTypes));

            if (!_methodsCalled.ContainsKey(nameof(SetTypes)))
            {
                // GetTypes was called before SetTypes, throw an exception with the timestamps of the call.
                var callHistoryAsString = string.Join("\n", _methodsCalled.Select(kvp =>
                    $"{kvp.Key}: {string.Join(", ", kvp.Value.Select(b => b.ToString("yyyy-MM-dd HH:mm:ss.fff")))}"));

                throw new InvalidOperationException(
                    $"GetTypes was called before SetTypes. Call history: {callHistoryAsString}");
            }

            return _types;
        }

        private static void LogCallHistory(string methodName)
        {
            _methodsCalled.AddOrUpdate(methodName, _ => new List<DateTime> { DateTime.UtcNow },
                (_, callHistory) =>
                {
                    callHistory.Add(DateTime.UtcNow);
                    return callHistory;
                });
        }

        internal void SetTypes(IEnumerable<Type> types)
        {
            if (types == null)
            {
                throw new ArgumentNullException(nameof(types));
            }

            LogCallHistory(nameof(SetTypes));
            _types = types.ToArray();
        }
    }
}
