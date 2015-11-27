// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class NameResolver : INameResolver
    {
        private readonly Random _rand = new Random();

        public string Resolve(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "rand-guid":
                    return Guid.NewGuid().ToString();
                case "rand-int":
                    return _rand.Next(10000, int.MaxValue).ToString();
            }

            return name;
        }
    }
}
