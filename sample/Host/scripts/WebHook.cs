// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Functions
{
    public static partial class Functions
    {
        public static async Task WebHook(HttpRequestMessage request)
        {
            string body = await request.Content.ReadAsStringAsync();
            Console.WriteLine(string.Format("C# WebHook function received message '{0}'", body));
        }
    }
}
