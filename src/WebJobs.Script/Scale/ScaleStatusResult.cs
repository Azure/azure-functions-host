// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Scale;

namespace Microsoft.Azure.WebJobs.Script.Scale
{
    /// <summary>
    /// Represents an aggregate scale status result returned from <see cref="FunctionsScaleManager.GetScaleStatusAsync(ScaleStatusContext)."/>
    /// </summary>
    /// <remarks>
    /// We use a separate model type for this rather than reusing <see cref="ScaleStatus"/> to decouple these contracts. This type
    /// is used as the contract with ScaleController and is serialized externally. <see cref="ScaleStatus"/> is the contract between
    /// the host and binding extensions.
    /// </remarks>
    public class ScaleStatusResult
    {
        public ScaleVote Vote { get; set; }
    }
}
