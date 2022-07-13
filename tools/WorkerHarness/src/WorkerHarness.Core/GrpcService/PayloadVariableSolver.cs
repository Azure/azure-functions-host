// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json.Nodes;
using WorkerHarness.Core.Commons;
using WorkerHarness.Core.Variables;

namespace WorkerHarness.Core.GrpcService
{
    public class PayloadVariableSolver : IPayloadVariableSolver
    {
        public bool TrySolveVariables(out JsonNode newPayload, JsonNode payload, IVariableObservable variableObservable)
        {
            try
            {
                JsonNode solvedPayload = payload.SolveVariables(variableObservable);
                newPayload = solvedPayload;

                return true;
            }
            catch (ArgumentException)
            {
                newPayload = new JsonObject();

                return false;
            }
        }
    }
}
