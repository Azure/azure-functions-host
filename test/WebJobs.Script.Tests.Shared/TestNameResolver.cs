// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestNameResolver : INameResolver
    {
        public TestNameResolver()
            : this(new Dictionary<string, string>())
        {
        }

        public TestNameResolver(Dictionary<string, string> names)
        {
            Names = names ?? throw new ArgumentNullException(nameof(names));
        }

        public Dictionary<string, string> Names { get; }

        public string Resolve(string name)
        {
            if (Names.TryGetValue(name, out string value))
            {
                return value;
            }
            throw new NotSupportedException(string.Format("Cannot resolve name: '{0}'", name));
        }
    }
}
