// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Config;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestScopedSettings : IDisposable
    {
        private readonly IDictionary<string, string> _variables;
        private readonly IDictionary<string, string> _existingVariables;
        private readonly ScriptSettingsManager _settingsManager;
        private bool _disposed = false;

        public TestScopedSettings(ScriptSettingsManager settingsManager, string name, string value)
            : this(settingsManager, new Dictionary<string, string> { { name, value } })
        {
        }

        public TestScopedSettings(ScriptSettingsManager settingsManager, IDictionary<string, string> variables)
        {
            _settingsManager = settingsManager;
            _variables = variables;
            _existingVariables = new Dictionary<string, string>(variables.Count);

            SetVariables();
        }

        private void SetVariables()
        {
            foreach (var item in _variables)
            {
                _existingVariables.Add(item.Key, _settingsManager.GetSetting(item.Key));

                _settingsManager.SetSetting(item.Key, item.Value);
            }
        }

        private void ClearVariables()
        {
            foreach (var item in _variables)
            {
                _settingsManager.SetSetting(item.Key, _existingVariables[item.Key]);
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
