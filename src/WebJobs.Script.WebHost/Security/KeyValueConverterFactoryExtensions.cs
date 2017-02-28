// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public static class KeyValueConverterFactoryExtensions
    {
        public static Key ReadKey(this IKeyValueConverterFactory factory, Key key)
        {
            IKeyValueReader reader = factory.GetValueReader(key);
            return reader.ReadValue(key);
        }

        public static Key WriteKey(this IKeyValueConverterFactory factory, Key key)
        {
            IKeyValueWriter writer = factory.GetValueWriter(key);
            return writer.WriteValue(key);
        }
    }
}
