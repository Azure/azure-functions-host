// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    internal class NameResolver : DefaultNameResolver
    {
        private readonly Random _rand = new Random();

        public override string Resolve(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            // resolve our special tokens
            switch (name.ToLowerInvariant())
            {
                case "rand-guid":
                    return Guid.NewGuid().ToString();
                case "rand-int":
                    return _rand.Next(10000, int.MaxValue).ToString(CultureInfo.InvariantCulture);
            }

            // if not one of the tokens we know how to resolve
            // call base
            string resolved = base.Resolve(name);
            if (resolved != null)
            {
                return resolved;
            }

            // contract is to return null if not found. 
            return null;
        }
    }
}
