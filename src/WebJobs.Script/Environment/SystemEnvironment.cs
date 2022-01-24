// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;

namespace Microsoft.Azure.WebJobs.Script
{
    public class SystemEnvironment : IEnvironment
    {
        private SystemEnvironment(IDictionary variables) : base(variables) { }

        public static SystemEnvironment Instance { get; private set; } = new SystemEnvironment(Environment.GetEnvironmentVariables());

        /// <summary>
        /// This is a method we can call in the event handler whenever an envinmental vairable changes, when we specialize, etc.
        /// This will trigger a re-cache of all environmental variables and properties.
        /// </summary>
        public void Recache() => Cache(Environment.GetEnvironmentVariables());

        public override void SetEnvironmentVariable(string name, string value)
        {
            Environment.SetEnvironmentVariable(name, value);
            Cache(Environment.GetEnvironmentVariables());
        }
    }
}
