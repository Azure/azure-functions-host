// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Models
{
    public enum FunctionAppContentEditingState
    {
        /// <summary>
        /// Host cannot determine if function app content is editable
        /// </summary>
        Unknown,

        /// <summary>
        /// Function app content is editable
        /// </summary>
        Allowed,

        /// <summary>
        /// Function app content is not editable
        /// </summary>
        NotAllowed
    }
}
