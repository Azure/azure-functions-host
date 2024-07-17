// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Extensions
{
    internal static class TaskExtensions
    {
        /// <summary>
        /// Forgets the task. This method is used to indicating not awaiting a task is intentional.
        /// </summary>
        /// <param name="task">The task to forget.</param>
        public static void Forget(this Task task)
        {
            // No op - this method is used to suppress the compiler warning.
        }
    }
}
