// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public sealed class PlaintextKeyValueConverter : IKeyValueReader, IKeyValueWriter
    {
        private readonly FileAccess _access;

        public PlaintextKeyValueConverter(FileAccess access)
        {
            _access = access;
        }

        public string ReadValue(Key key)
        {
            ValidateAccess(FileAccess.Read);

            return key.Value;
        }

        public Key WriteValue(Key key)
        {
            ValidateAccess(FileAccess.Write);

            return new Key(key.Name, key.Value);
        }

        private void ValidateAccess(FileAccess access)
        {
            if (!_access.HasFlag(access))
            {
                throw new InvalidOperationException($"The current {nameof(PlaintextKeyValueConverter)} does not support {access.ToString("G")} access.");
            }
        }
    }
}
