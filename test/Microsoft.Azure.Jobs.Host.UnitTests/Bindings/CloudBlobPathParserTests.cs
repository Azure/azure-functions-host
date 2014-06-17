using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Bindings
{
    public class CloudBlobPathParserTests
    {
        [Fact]
        public void TestEquals()
        {
            CloudBlobPath path1 = new CloudBlobPath(@"container/dir/subdir/{name}.csv");
            CloudBlobPath path2 = new CloudBlobPath(@"container/dir/subdir/{name}.csv");
            CloudBlobPath path3 = new CloudBlobPath(@"container/dir/subdir/other.csv");

            Assert.Equal(path1, path2);
            Assert.NotEqual(path2, path3);

            Assert.Equal(path1.GetHashCode(), path2.GetHashCode());
            Assert.NotEqual(path1.GetHashCode(), path3.GetHashCode()); // statement about hashcode quality. 
        }

        private static IDictionary<string, string> Match(string a, string b)
        {
            var pa = new CloudBlobPath(a);
            var pb = new CloudBlobPath(b);
            return pa.Match(pb);
        }

        [Fact]
        public void TestMethod1()
        {
            var d = Match("container", "container");
            Assert.NotNull(d);
            Assert.Equal(0, d.Count);
        }

        [Fact]
        public void TestMethod2()
        {
            var d = Match(@"container/blob", @"container/blob");
            Assert.NotNull(d);
            Assert.Equal(0, d.Count);
        }

        [Fact]
        public void TestMethod3()
        {
            var d = Match(@"container/{name}.csv", @"container/foo.csv");
            Assert.NotNull(d);
            Assert.Equal(1, d.Count);
            Assert.Equal("foo", d["name"]);
        }

        [Fact]
        public void TestMethod4()
        {
            // Test corner case where matching at end
            var d = Match(@"container/{name}", @"container/foo.csv");
            Assert.NotNull(d);
            Assert.Equal(1, d.Count);
            Assert.Equal("foo.csv", d["name"]);
        }


        [Fact]
        public void TestMethodExtension()
        {
            // {name} is greedy when matching up to an extension. 
            var d = Match(@"container/{name}.csv", @"container/foo.alpha.csv");
            Assert.NotNull(d);
            Assert.Equal(1, d.Count);
            Assert.Equal("foo.alpha", d["name"]);
        }

        [Fact]
        public void TestNotGreedy()
        {
            // Test non-greedy matching. May want to change the policy here. 

            var d = Match(@"container/{a}.{b}", @"container/foo.alpha.beta.csv");
            Assert.NotNull(d);
            Assert.Equal(2, d.Count);
            Assert.Equal("foo", d["a"]);
            Assert.Equal("alpha.beta.csv", d["b"]);
        }

        [Fact]
        public void TestMethod6()
        {
            // Test corner case where matching on last 
            var d = Match(@"daas-test-input/{name}.txt", @"daas-test-input/bob.txtoutput");
            Assert.Null(d);
        }

        [Fact]
        public void TestMethod5()
        {
            // Test corner case where matching on last 
            var d = Match(@"container/{name}-{date}.csv", @"container/foo-Jan1st.csv");
            Assert.NotNull(d);
            Assert.Equal(2, d.Count);
            Assert.Equal("foo", d["name"]);
            Assert.Equal("Jan1st", d["date"]);
        }

        [Fact]
        public void GetNames()
        {
            var path = new CloudBlobPath(@"container/{name}-{date}.csv");
            var d = path.GetParameterNames();
            var names = d.ToArray();

            Assert.Equal(2, names.Length);
            Assert.Equal("name", names[0]);
            Assert.Equal("date", names[1]);
        }

        [Fact]
        public void Apply1()
        {
            var d = new Dictionary<string, string> {{"name", "value"}};
            string result = new CloudBlobPath("container").ApplyNames(d).ToString();
            Assert.Equal("container", result);
        }

        [Fact]
        public void Apply2()
        {
            var d = new Dictionary<string, string> {{"name", "value"}};
            string result = new CloudBlobPath(@"container/{name}").ApplyNames(d).ToString();
            Assert.Equal(@"container/value", result);
        }

        [Fact]
        public void Apply3()
        {
            var d = new Dictionary<string, string> {{"name", "value"}};
            Assert.Throws<InvalidOperationException>(() => new CloudBlobPath(@"container/{missing}").ApplyNames(d).ToString());
        }

        [Fact]
        public void Apply4()
        {
            var d = new Dictionary<string, string> {{"name", "value"}};
            Assert.Throws<InvalidOperationException>(() => new CloudBlobPath(@"container/{missing").ApplyNames(d).ToString());
        }
    }
}
