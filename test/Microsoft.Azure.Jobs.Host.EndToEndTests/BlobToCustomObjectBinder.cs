using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Microsoft.Azure.Jobs.Host.EndToEndTests
{
    /// <summary>
    /// Converts blobs to/from <see cref="Microsoft.Azure.Jobs.Host.EndToEndTests.CustomObject"/>
    /// </summary>
    public class BlobToCustomObjectBinder : ICloudBlobStreamBinder<CustomObject>
    {
        public CustomObject ReadFromStream(Stream input)
        {
            using (StreamReader reader = new StreamReader(input))
            {
                string jsonString = reader.ReadToEnd();
                return JsonConvert.DeserializeObject<CustomObject>(jsonString);
            };
        }

        public void WriteToStream(CustomObject value, Stream output)
        {
            using (StreamWriter writer = new StreamWriter(output))
            {
                string jsonString = JsonConvert.SerializeObject(value);
                writer.Write(jsonString);
            };
        }
    }
}
