using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WebJobs.Script.Cli.Common
{
    internal class Template
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("function")]
        public JObject  Function { get; set; }

        [JsonProperty("metadata")]
        public TemplateMetadata Metadata { get; set; }

        [JsonProperty("files")]
        public Dictionary<string, string> Files { get; set; }
    }

    internal class TemplateMetadata
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("defaultFunctionName")]
        public string DefaultFunctionName { get; set; }

        [JsonProperty("language")]
        public string Language { get; set; }

        [JsonProperty("userPrompt")]
        public IEnumerable<string> UserPrompt { get; set; }
    }
}
