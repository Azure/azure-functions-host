// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Dashboard.Data
{
    internal class SimpleConcurrentDocument<T> : IConcurrentDocument<T>
    {
        public T Document
        {
            get; set;
        }

        public string ETag
        {
            get
            {
                return "";
            }
        }
    }
}