// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestEnvironment : IEnvironment
    {
        public TestEnvironment() : this(new Hashtable()) { }

        public TestEnvironment(Dictionary<string, string> variables) : this()
        {
            foreach (var pair in variables)
            {
                VariableCache[pair.Key] = pair.Value;
            }
            Rehydrate();
        }

        public TestEnvironment(IDictionary variables) : base(variables)
        {
        }

        public void Clear() => VariableCache.Clear();

        public override void SetEnvironmentVariable(string name, string value) => Set(name, value);

        private void Set(string name, string value)
        {
            if (value == null && VariableCache[name] != null)
            {
                VariableCache.Remove(name);
                return;
            }

            VariableCache[name] = value;

            Rehydrate();
        }

        internal void SetAzureWebsiteName(string value) => Set(EnvironmentSettingNames.AzureWebsiteName, value);

        internal void SetAzureWebsiteOwnerName(string value) => Set(EnvironmentSettingNames.AzureWebsiteOwnerName, value);

        internal void SetAzureWebsiteSlotName(string value) => Set(EnvironmentSettingNames.AzureWebsiteSlotName, value);
    }
}
