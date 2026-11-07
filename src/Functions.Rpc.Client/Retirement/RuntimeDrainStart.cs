// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Azure.Functions.Rpc.Client;

internal sealed record RuntimeDrainStart(
    IReadOnlyList<string> NonHttpWorkerIds,
    IReadOnlyList<string> HttpWorkerIds);
