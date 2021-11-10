// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Runtime.Serialization;

namespace Microsoft.Azure.WebJobs.Script.Models
{
    public enum FunctionAppContentEditable
    {
        /// <summary>
        /// Function app content is editable
        /// </summary>
        [EnumMember(Value = "true")]
        Editable,

        /// <summary>
        /// Function app content is not editable
        /// </summary>
        [EnumMember(Value = "false")]
        NotEditable,

        /// <summary>
        /// Host cannot determine if function app content is editable
        /// </summary>
        [EnumMember(Value = "unknown")]
        Unknown
    }
}
