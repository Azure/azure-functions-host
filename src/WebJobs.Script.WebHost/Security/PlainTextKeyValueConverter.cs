// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public sealed class PlaintextKeyValueConverter : KeyValueConverter, IKeyValueReader, IKeyValueWriter
    {
        public PlaintextKeyValueConverter(FileAccess access)
            : base(access)
        {
        }

        public Key ReadValue(Key key)
        {
            ValidateAccess(FileAccess.Read);

            return new Key(key.Name, key.Value);
        }

        public Key WriteValue(Key key)
        {
            ValidateAccess(FileAccess.Write);

            return new Key(key.Name, key.Value);
        }
    }
}
