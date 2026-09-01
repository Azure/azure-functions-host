// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.WorkerProxy;

/// <summary>
/// Identifies why a relay stream attachment was rejected.
/// </summary>
internal enum FunctionRpcRelayAttachmentFailure
{
    /// <summary>
    /// The requested side already has an active stream.
    /// </summary>
    Duplicate,

    /// <summary>
    /// The previous relay session has terminated but has not fully released.
    /// </summary>
    PreviousSessionTearingDown,

    /// <summary>
    /// WorkerProxy shutdown has started and no new streams are accepted.
    /// </summary>
    Shutdown
}
