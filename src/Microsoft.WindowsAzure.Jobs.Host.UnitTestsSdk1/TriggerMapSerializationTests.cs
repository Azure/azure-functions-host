using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Jobs;
using Newtonsoft.Json;

namespace Microsoft.WindowsAzure.Jobs.UnitTestsSdk1
{
    [TestClass]
    public class TriggerMapSerializationTests
    {
        [TestMethod]
        public void SerializeCloudBlobPathAsString()
        {
            // CloudPath serializes just like string 
            string str = @"container/dir/{name}.txt";

            var path = new CloudBlobPath(str);

            // Does not require a custom settings object.
            string json1 = JsonConvert.SerializeObject(path);
            string json2 = JsonConvert.SerializeObject(str);

            Assert.AreEqual(json2, json1);

            var path2 = JsonConvert.DeserializeObject<CloudBlobPath>(json1);
            Assert.AreEqual(path.ContainerName, path2.ContainerName);
            Assert.AreEqual(path.BlobName, path2.BlobName);
        }

        [TestMethod]
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
            AssertStringEqual(expected, actual);

            var map2 = TriggerMap.LoadJson(json);
                        
            
            // Round-trips.
            var json2 = TriggerMap.SaveJson(map2);
            Assert.AreEqual(json, json2);
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

        // Assert strings are the same. If they're different. Give a more useful error message. Useful for long strings. 
        static void AssertStringEqual(string a, string b)
        {
            if (a == b)
            {
                return;
            }
            int i = 0;

            while (true)
            {
                if (i >= a.Length)
                {
                    break;
                }
                if (i >= b.Length)
                {
                    break;
                }
                Assert.AreEqual(a[i], b[i], string.Format("difference at index {0}", i));
                i++;
            }
            Assert.AreEqual(a, b); // catch all                        
        }

        const string JsonSaved = @"{
  'Storage': {
    'http://scope1': [
      {
        '$type': 'Microsoft.WindowsAzure.Jobs.BlobTrigger, Microsoft.WindowsAzure.Jobs.Host',
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
        '$type': 'Microsoft.WindowsAzure.Jobs.QueueTrigger, Microsoft.WindowsAzure.Jobs.Host',
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
