using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    internal class RpcActionMessage
    {
        public string Id { get; set; } = string.Empty;

        public string MessageType { get; set; } = string.Empty;

        public string Direction { get; set; } = string.Empty;

        public IEnumerable<MatchingContext>? MatchingCriteria { get; set; }

        public IEnumerable<ValidationContext>? Validators { get; set; }

        public JsonNode? Payload { get; set; }
    }
}
