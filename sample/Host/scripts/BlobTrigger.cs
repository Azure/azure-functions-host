// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Newtonsoft.Json;

namespace Host
{
    public static partial class Functions
    {
        public static void BlobTrigger(string postJson)
        {
            Post post = JsonConvert.DeserializeObject<Post>(postJson);

            Console.WriteLine(string.Format("C# BlobTrigger function processed post '{0}'", post.Text));
        }
    }
}
