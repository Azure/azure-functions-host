// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Jobs.Host.Bindings;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Bindings
{
    public class BindingDataTests
    {
        [Fact]
        public void GetContractFromObjectParamType()
        {
            var names = BindingData.GetContract(typeof(object));
            Assert.Null(names);
        }

        [Fact]
        public void GetContractFromIntParamType()
        {
            // Simple type, not structured, doesn't produce any route parameters.
            var names = BindingData.GetContract(typeof(int));
            Assert.Null(names);
        }

        [Fact]
        public void GetContractFromStructuredParamType()
        {
            var names = BindingData.GetContract(typeof(Complex)).Keys.ToArray();

            Array.Sort(names);
            var expected = new string[] { "IntProp", "StringProp" };
            AssertArrayEqual(expected, names);
        }

        [Fact]
        public void GetBindingDataSimple()
        {
            // When JSON is a structured object, we can extract the fields as route parameters.
            string json = @"{ ""Name"" : 12, ""other"" : 13 }";
            var p = BindingData.GetBindingData(json, BindingData.GetContract(typeof(NameClass)));

            Assert.Equal(1, p.Count);
            Assert.Equal(12, p["Name"]);
        }

        [Fact]
        public void GetBindingDataParseError()
        {
            // Test when payload is not a json object, does not produce any route parameters
            foreach (var json in new string[] { 
                "12",
                "[12]", 
                "x",
                "\\", 
                string.Empty,
                null
            })
            {
                var p = BindingData.GetBindingData(json, BindingData.GetContract(typeof(NameClass)));
                // Shouldn't get route params from json:
                Assert.Null(p);
            }
        }

        [Fact]
        public void GetBindingDataComplexJson()
        {
            // 
            // When JSON is a structured object, we can extract the fields as route parameters.
            string json = @"{
""a"":1,
""b"":[1,2,3],
""c"":{}
}";
            var p = BindingData.GetBindingData(json, BindingData.GetContract(typeof(AbcClass)));

            // Only take simple types
            Assert.Equal(1, p.Count);
            Assert.Equal(1, p["a"]);
        }

        [Fact]
        public void GetBindingDataDate()
        {
            // Dates with JSON can be tricky. Test Date serialization.
            DateTime date = new DateTime(1950, 6, 1, 2, 3, 30);

            var json = JsonConvert.SerializeObject(new { date = date });

            var p = BindingData.GetBindingData(json, BindingData.GetContract(typeof(DateClass)));

            Assert.Equal(1, p.Count);
            Assert.Equal(date, p["date"]);
        }


        // Helper for comparing arrays
        static void AssertArrayEqual(string[] a, string[] b)
        {
            Assert.Equal(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
            {
                Assert.Equal(a[i], b[i]);
            }
        }

        private class NameClass
        {
            public int Name { get; set; }
        }

        private class AbcClass
        {
            public int a { get; set; }

            public int[] b { get; set; }

            public IDictionary<string, object> c { get; set; }
        }

        private class DateClass
        {
            public DateTime date { get; set; }
        }

        // Type to test yielding route parameters.
        // Only simpl
        class Complex
        {
            public int _field = 0; // skip: not a property

            public int IntProp { get; set; } // Yes

            public string StringProp { get; set; } // Yes

            public Complex Nexted { get; set; } // skip: not simple type

            public static int StaticIntProp { get; set; } // skip: not instance property

            private int PrivateIntProp { get; set; } // skip: private property
        }
    }
}
