// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Azure.WebJobs.Script;

namespace Microsoft.WebJobs.Script.Tests
{
    public class TestScriptJobHostEnvironment : IScriptJobHostEnvironment
    {
        private readonly IDictionary<string, string> _variables;

        public string GetEnvironmentVariable(string name)
        {
            return string.Empty;
        }

        public void SetEnvironmentVariable(string name, string value)
        {
        }

        public void RestartHost()
        {
            throw new NotImplementedException();
        }

        public void Shutdown()
        {
            throw new NotImplementedException();
        }
    }
}
