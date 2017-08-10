// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public interface ILanguageInvoker
    {
        // TODO - Move to Reactive extensions
        // Events Messages - Messages coming from the language hosts
        // Host processes messages for logs
        // event EventHandler<LanguageInvokerMessagesEventArgs> LanguageInvokerMessagesUpdated;

        /// <summary>
        /// Invoke the function using the specified parameters.
        /// </summary>
        /// <param name="parameters">The parameters.</param>
        /// <returns>A <see cref="Task"/> for the invocation.</returns>
        Task Invoke(object[] parameters);
    }
}
