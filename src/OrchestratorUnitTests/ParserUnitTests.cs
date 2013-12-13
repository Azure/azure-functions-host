using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Orchestrator;
using System.Linq;
using RunnerInterfaces;
using Newtonsoft.Json;

namespace OrchestratorUnitTests
{
    [TestClass]
    public class ParserTests
    {
        [TestMethod]
        public void TestEquals()
        {
            CloudBlobPath path1 = new CloudBlobPath(@"container\dir\subdir\{name}.csv");
            CloudBlobPath path2 = new CloudBlobPath(@"container\dir\subdir\{name}.csv");
            CloudBlobPath path3 = new CloudBlobPath(@"container\dir\subdir\other.csv");

            Assert.AreEqual(path1, path2);
            Assert.AreNotEqual(path2, path3);

            Assert.AreEqual(path1.GetHashCode(), path2.GetHashCode());
            Assert.AreNotEqual(path1.GetHashCode(), path3.GetHashCode()); // statement about hashcode quality. 
        }

        [TestMethod]
        public void TestJsonSerialization()
        {
            CloudBlobPath path = new CloudBlobPath(@"container\dir\subdir\{name}.csv");

            string json = JsonConvert.SerializeObject(path);

            string d1 = JsonConvert.DeserializeObject<string>(json);

            CloudBlobPath d2 = JsonConvert.DeserializeObject<CloudBlobPath>(json);

            Assert.AreEqual(path, d2);
            Assert.AreEqual(d1, d2.ToString());
            Assert.AreEqual(path, new CloudBlobPath(d1));
        }

        static IDictionary<string,string> Match(string a, string b)
        {
            var pa = new CloudBlobPath(a);
            var pb = new CloudBlobPath(b);
            return pa.Match(pb);
        }

        [TestMethod]
        public void TestMethod1()
        {
            var d = Match("container", "container");
            Assert.IsNotNull(d);
            Assert.AreEqual(0, d.Count);
        }

        [TestMethod]
        public void TestMethod2()
        {
            var d = Match(@"container\blob", @"container\blob");
            Assert.IsNotNull(d);
            Assert.AreEqual(0, d.Count);
        }

        [TestMethod]
        public void TestMethod3()
        {
            var d = Match(@"container\{name}.csv", @"container\foo.csv");
            Assert.IsNotNull(d);
            Assert.AreEqual(1, d.Count);
            Assert.AreEqual("foo", d["name"]);
        }

        [TestMethod]
        public void TestMethod4()
        {
            // Test corner case where matching at end
            var d = Match(@"container\{name}", @"container\foo.csv");
            Assert.IsNotNull(d);
            Assert.AreEqual(1, d.Count);
            Assert.AreEqual("foo.csv", d["name"]);
        }
        

        [TestMethod]
        public void TestMethodExtension()
        {
            // {name} is greedy when matching up to an extension. 
            var d = Match(@"container\{name}.csv", @"container\foo.alpha.csv");
            Assert.IsNotNull(d);
            Assert.AreEqual(1, d.Count);
            Assert.AreEqual("foo.alpha", d["name"]);
        }
        [TestMethod]
        public void TestNotGreedy()
        {
            // Test non-greedy matching. May want to change the policy here. 

            var d = Match(@"container\{a}.{b}", @"container\foo.alpha.beta.csv");
            Assert.IsNotNull(d);
            Assert.AreEqual(2, d.Count);
            Assert.AreEqual("foo", d["a"]);
            Assert.AreEqual("alpha.beta.csv", d["b"]);
        }

        [TestMethod]
        public void TestMethod6()
        {
            // Test corner case where matching on last 
            var d = Match(@"daas-test-input\{name}.txt", @"daas-test-input\bob.txtoutput");
            Assert.IsNull(d);            
        }

        [TestMethod]
        public void TestMethod5()
        {
            // Test corner case where matching on last 
            var d = Match(@"container\{name}-{date}.csv", @"container\foo-Jan1st.csv");
            Assert.IsNotNull(d);
            Assert.AreEqual(2, d.Count);
            Assert.AreEqual("foo", d["name"]);
            Assert.AreEqual("Jan1st", d["date"]);
        }

        [TestMethod]
        public void GetNames()
        {
            var path = new CloudBlobPath(@"container\{name}-{date}.csv");
            var d = path.GetParameterNames();
            var names = d.ToArray();

            Assert.AreEqual(2, names.Length);
            Assert.AreEqual("name", names[0]);
            Assert.AreEqual("date", names[1]);
        }

        [TestMethod]
        public void Apply1()
        {
            var d = new Dictionary<string, string> { { "name", "value" } };
            string result = new CloudBlobPath("container").ApplyNames(d).ToString();
            Assert.AreEqual("container", result);
        }

        [TestMethod]
        public void Apply2()
        {
            var d = new Dictionary<string, string> { { "name", "value" } };
            string result = new CloudBlobPath(@"container\{name}").ApplyNames(d).ToString();
            Assert.AreEqual(@"container\value", result);
        }

        [TestMethod]
        public void Apply3()
        {
            var d = new Dictionary<string, string> { { "name", "value" } };

            try
            {
                string result = new CloudBlobPath(@"container\{missing}").ApplyNames(d).ToString();
                Assert.Fail("Should have thrown");
            }
            catch (InvalidOperationException)
            {
            }            
        }

        [TestMethod]
        public void Apply4()
        {
            var d = new Dictionary<string, string> { { "name", "value" } };

            try
            {
                // Parser error.
                string result = new CloudBlobPath(@"container\{missing").ApplyNames(d).ToString();
                Assert.Fail("Should have thrown");
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
