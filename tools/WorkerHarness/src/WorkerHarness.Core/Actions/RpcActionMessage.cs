// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json.Nodes;
using WorkerHarness.Core.Matching;
using WorkerHarness.Core.Validators;

namespace WorkerHarness.Core.Actions
{
    internal sealed class RpcActionMessageTypes
    {
        public static string Incoming = "incoming";

        public static string Outgoing = "outgoing";
    }

    public sealed class RpcActionMessage
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string MessageType { get; set; } = string.Empty;

        public string Direction { get; set; } = string.Empty;

        public IEnumerable<MatchingContext> MatchingCriteria { get; set; } = new List<MatchingContext>();

        public IEnumerable<ValidationContext> Validators { get; set; } = new List<ValidationContext>();

        public JsonNode? Payload { get; set; }

        public IDictionary<string, string>? SetVariables { get; set; }
    }
}
