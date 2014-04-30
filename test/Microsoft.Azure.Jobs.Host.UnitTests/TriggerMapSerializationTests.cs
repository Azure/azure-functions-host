using System.Text;
using Microsoft.Azure.Jobs;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests
{
    public class TriggerMapSerializationTests
    {
        [Fact]
        public void SerializeCloudBlobPathAsString()
        {
            // CloudPath serializes just like string 
            string str = @"container/dir/{name}.txt";

            var path = new CloudBlobPath(str);

            // Does not require a custom settings object.
            string json1 = JsonConvert.SerializeObject(path);
            string json2 = JsonConvert.SerializeObject(str);

            Assert.Equal(json2, json1);

            var path2 = JsonConvert.DeserializeObject<CloudBlobPath>(json1);
            Assert.Equal(path.ContainerName, path2.ContainerName);
            Assert.Equal(path.BlobName, path2.BlobName);
        }

        [Fact]
        public void SerializeTriggerMap()
        {
            var map = new TriggerMap();

            var acs ="a=1;b=2";
            var scope1 = "http://scope1";
            var scope2 = "http://scope2";

            map.AddTriggers(scope1,
                new BlobTrigger
                {
                    AccountConnectionString = acs,
                    BlobInput = new CloudBlobPath("container/input/{name}.txt"),
                    BlobOutputs = new CloudBlobPath[] 
                    {
                        new CloudBlobPath("container/output/{name}.txt"),
                        new CloudBlobPath("container/output2/{name}.txt")
                    },
                    CallbackPath = "http://callback?type=blob"
                });
            map.AddTriggers(scope2,
                new QueueTrigger
                {
                    AccountConnectionString = acs,
                    CallbackPath = "http://callback?type=queue",
                    QueueName = "myqueue"
                }
                );

            var json = TriggerMap.SaveJson(map);

            // Since this is a serialization format, check it against a baseline to detect changes.
            string expected = Normalize(JsonSaved.Replace('\'', '"'));
            string actual = Normalize(json);
            Assert.Equal(expected, actual);

            var map2 = TriggerMap.LoadJson(json);
                        
            
            // Round-trips.
            var json2 = TriggerMap.SaveJson(map2);
            Assert.Equal(json, json2);
        }

        // Normalize the whitespace. This can vary with different serializers. 
        static string Normalize(string json)
        {
            StringBuilder sb = new StringBuilder();

            bool isLastCharWhitespace = true;
            foreach (char ch in json)
            {
                bool isWhitespace = ch == ' ' || ch == '\r' || ch == '\n' || ch == '\t';

                if (isWhitespace && isLastCharWhitespace)
                {
                    // skip
                }
                else if (isWhitespace)
                {
                    sb.Append(' '); // normalize to space
                }
                else
                {
                    sb.Append(ch);
                }

                isLastCharWhitespace = isWhitespace;
            }
            return sb.ToString();
        }

        const string JsonSaved = @"{
  'Storage': {
    'http://scope1': [
      {
        '$type': 'Microsoft.Azure.Jobs.BlobTrigger, Microsoft.Azure.Jobs.Host',
        'BlobInput': 'container/input/{name}.txt',
        'BlobOutputs': [
          'container/output/{name}.txt',
          'container/output2/{name}.txt'
        ],
        'CallbackPath': 'http://callback?type=blob',
        'AccountConnectionString': 'a=1;b=2',
        'Type': 'Blob'
      }
    ],
    'http://scope2': [
      {
        '$type': 'Microsoft.Azure.Jobs.QueueTrigger, Microsoft.Azure.Jobs.Host',
        'QueueName': 'myqueue',
        'CallbackPath': 'http://callback?type=queue',
        'AccountConnectionString': 'a=1;b=2',
        'Type': 'Queue'
      }
    ]
  }
}";
    }
}
