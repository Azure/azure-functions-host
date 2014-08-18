// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Host.TestCommon
{
    public class RandomNameResolver : INameResolver
    {
        // Convert to lowercase because many Azure services expect only lowercase
        private readonly string _randomString = Guid.NewGuid().ToString("N").ToLower();

        public string Resolve(string name)
        {
            if (name == "rnd")
            {
                return _randomString;
            }

            throw new NotSupportedException("Cannot resolve name: " + name);
        }

        public string ResolveInString(string input)
        {
            return input.Replace("%rnd%", _randomString);
        }
    }
}
