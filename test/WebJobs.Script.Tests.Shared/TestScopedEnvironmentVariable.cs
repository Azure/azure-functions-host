// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestScopedEnvironmentVariable : IDisposable
    {
        private readonly IDictionary<string, string> _variables;
        private readonly IDictionary<string, string> _existingVariables;
        private bool _disposed = false;

        public TestScopedEnvironmentVariable(string name, string value)
            : this(new Dictionary<string, string> { { name, value } })
        {
        }

        public TestScopedEnvironmentVariable(IDictionary<string, string> variables)
        {
            _variables = variables;
            _existingVariables = new Dictionary<string, string>(variables.Count);

            SetVariables();
        }

        private void SetVariables()
        {
            foreach (var item in _variables)
            {
                _existingVariables.Add(item.Key, Environment.GetEnvironmentVariable(item.Key));

                Environment.SetEnvironmentVariable(item.Key, item.Value);
            }
        }

        private void ClearVariables()
        {
            foreach (var item in _variables)
            {
                Environment.SetEnvironmentVariable(item.Key, _existingVariables[item.Key]);
            }

            _existingVariables.Clear();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                ClearVariables();

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
