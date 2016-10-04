// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Logging
{
    /// <summary>
    /// log writer. 
    /// </summary>
    public interface ILogWriter
    {
        /// <summary>
        /// Log the given item.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task AddAsync(FunctionInstanceLogItem item, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Flush batched up entries. 
        /// </summary>
        /// <returns></returns>
        Task FlushAsync(CancellationToken cancellationToken = default(CancellationToken));
    }
}