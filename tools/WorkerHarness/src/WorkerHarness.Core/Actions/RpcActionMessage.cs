using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    public class RpcActionMessage
    {
        public string Id { get; set; } = string.Empty;

        public string MessageType { get; set; } = string.Empty;

        public string Direction { get; set; } = string.Empty;

        public IEnumerable<MatchingContext> MatchingCriteria { get; set; } = new List<MatchingContext>();

        public IEnumerable<ValidationContext> Validators { get; set; } = new List<ValidationContext>();

        public JsonNode? Payload { get; set; }

        public IDictionary<string, string>? SetVariables { get; set; }

        /// <summary>
        /// Check whether all variable dependencies in Match and Validators have been resolved
        /// </summary>
        /// <returns></returns>
        internal bool DependenciesResolved()
        {
            foreach (var match in MatchingCriteria)
            {
                if (!match.Resolved)
                {
                    return false;
                }
            }

            foreach (var validator in Validators)
            {
                if (!validator.Resolved)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
