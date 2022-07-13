// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json.Nodes;
using WorkerHarness.Core.Variables;

namespace WorkerHarness.Core.GrpcService
{
    /// <summary>
    /// Abtraction to solve variables in a message payload
    /// </summary>
    public interface IPayloadVariableSolver
    {
        /// <summary>
        /// Solve variables in a JsonNode payload.
        /// </summary>
        /// <param name="newPayload" cref="JsonNode">a new payload where all variables in payload are solved; empty payload otherwise</param>
        /// <param name="payload" cref="JsonNode">a payload that has zero or more variables</param>
        /// <param name="variableObservable" cref="IVariableObservable">store variables and their values</param>
        /// <returns>true if all variables in payload are solved, false otherwise</returns>
        bool TrySolveVariables(out JsonNode newPayload, JsonNode payload, IVariableObservable variableObservable);
    }
}
