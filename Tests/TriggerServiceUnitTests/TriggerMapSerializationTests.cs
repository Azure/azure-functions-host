using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System.Linq;
using TriggerService;
using TriggerService.Internal;

namespace TriggerServiceUnitTests
{
    [TestClass]
    public class TriggerMapSerializationTests
    {
        [TestMethod]
        public void SerializeCloudBlobPathAsString()
        {
            // CloudPath serializes just like string 
            string str = @"container\dir\{name}.txt";

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
                },
                new TimerTrigger
                {
                    CallbackPath = "http://callback?type=timer",
                    Interval = TimeSpan.FromMinutes(30)
                }
                );

            var json = TriggerMap.SaveJson(map);

            // Since this is a serialization format, check it against a baseline to detect changes.
            Assert.AreEqual(JsonSaved.Replace('\'', '"'), json);

            var map2 = TriggerMap.LoadJson(json);
                        
            
            // Round-trips.
            var json2 = TriggerMap.SaveJson(map2);
            Assert.AreEqual(json, json2);
        }


        const string JsonSaved = @"{
  'Storage': {
    'http://scope1': [
      {
        '$type': 'TriggerService.BlobTrigger, TriggerService',
        'BlobInput': 'container\\input\\{name}.txt',
        'BlobOutputs': [
          'container\\output\\{name}.txt',
          'container\\output2\\{name}.txt'
        ],
        'CallbackPath': 'http://callback?type=blob',
        'AccountConnectionString': 'a=1;b=2',
        'Type': 'Blob'
      }
    ],
    'http://scope2': [
      {
        '$type': 'TriggerService.QueueTrigger, TriggerService',
        'QueueName': 'myqueue',
        'CallbackPath': 'http://callback?type=queue',
        'AccountConnectionString': 'a=1;b=2',
        'Type': 'Queue'
      },
      {
        '$type': 'TriggerService.TimerTrigger, TriggerService',
        'Interval': '00:30:00',
        'CallbackPath': 'http://callback?type=timer',
        'Type': 'Timer'
      }
    ]
  }
}";
    }
}