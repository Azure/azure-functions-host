// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Data.Entity;

namespace Host
{
    public static partial class Functions
    {
        public static void QueueTrigger(Post post)
        {
            DbContext context = new DbContext("myconnection");

            Console.WriteLine(string.Format("C# QueueTrigger function processed post '{0}'", post.Text));
        }
    }
}
