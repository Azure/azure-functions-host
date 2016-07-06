using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Kudu
{
    public class VfsStatEntry
    {
        [JsonProperty(PropertyName = "name")]
        public string Name { get; set; }

        [JsonProperty(PropertyName = "size")]
        public long Size { get; set; }

        [JsonProperty(PropertyName = "mtime")]
        public DateTimeOffset MTime { get; set; }

        [JsonProperty(PropertyName = "crtime")]
        public DateTimeOffset CRTime { get; set; }

        [JsonProperty(PropertyName = "mime")]
        public string Mime { get; set; }

        [JsonProperty(PropertyName = "href")]
        public string Href { get; set; }

        [JsonProperty(PropertyName = "path")]
        public string Path { get; set; }
    }
}
