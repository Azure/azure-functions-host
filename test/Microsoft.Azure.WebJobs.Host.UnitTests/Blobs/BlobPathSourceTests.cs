// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Blobs;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Blobs
{
    public class BlobPathSourceTests
    {
        [Fact]
        public void TestToString()
        {
            IBlobPathSource path1 = BlobPathSource.Create(@"container/dir/subdir/{name}.csv");
            IBlobPathSource path2 = BlobPathSource.Create(@"container/dir/subdir/{name}.csv");
            IBlobPathSource path3 = BlobPathSource.Create(@"container/dir/subdir/other.csv");

            Assert.Equal(path1.ToString(), path2.ToString());
            Assert.NotEqual(path2.ToString(), path3.ToString());
        }

        private static IDictionary<string, string> Match(string a, string b)
        {
            var pathA = BlobPathSource.Create(a);
            var pathB = BlobPath.Parse(b);

            IReadOnlyDictionary<string, object> bindingData = pathA.CreateBindingData(pathB);

            if (bindingData == null)
            {
                return null;
            }

            IDictionary<string, string> matches = new Dictionary<string, string>();

            foreach (KeyValuePair<string, object> item in bindingData)
            {
                matches.Add(item.Key, item.Value.ToString());
            }

            return matches;
        }

        [Fact]
        public void TestMethod1()
        {
            var d = Match("container", "container/item");
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
        public void TestNonGreedy()
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
            var path = BlobPathSource.Create(@"container/{name}-{date}.csv");
            var d = path.ParameterNames;
            var names = d.ToArray();

            Assert.Equal(2, names.Length);
            Assert.Equal("name", names[0]);
            Assert.Equal("date", names[1]);
        }
    }
}
