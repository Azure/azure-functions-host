// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    public class ObjectBinderHelpersTests
    {
        [Fact]
        public void ConvertAnonymousType()
        {
            var d = ObjectBinderHelpers.ConvertObjectToDict(new { A = 'A', One = 1 });

            Assert.Equal(2, d.Count);
            Assert.Equal("A", d["A"]);
            Assert.Equal("1", d["One"]);
        }

        [Fact]
        public void RoundTripDateUTc()
        {
            var now = DateTime.UtcNow; // Utc
            Assert.Equal(now.ToString(), now.ToUniversalTime().ToString());

            var val = RoundTripDateTime(now);

            // Full quality (including Kind)
            Assert.Equal(now, val);
        }
        
        [Fact]
        public void RoundTripDateLocal()
        {
            var now = DateTime.Now; // Local
            
            var val = RoundTripDateTime(now);
            
            // Full quality (including Kind)
            Assert.Equal(now.ToUniversalTime(), val);
        }
        
        private static DateTime RoundTripDateTime(DateTime now)
        {
            var d = ObjectBinderHelpers.ConvertObjectToDict(new DateWrapper { Value = now });

            Assert.Equal(1, d.Count);

            // Note RawString is in UTC format
            var rawVal = d["Value"];
            var dateRaw = DateTime.Parse(rawVal);
            Assert.Equal(now.ToUniversalTime(), dateRaw.ToUniversalTime());
            
            DateTime val = ObjectBinderHelpers.ConvertDictToObject<DateWrapper>(d).Value;

            // should have normalized Kind to be UTC
            Assert.Equal(val.Kind, DateTimeKind.Utc);
            Assert.Equal(now.ToUniversalTime(), val.ToUniversalTime());

            return val;
        }

        [Fact]
        public void ReadEmptyNullableDate()
        {
            // Empty
            var d = new Dictionary<string, string>();
            var obj = ObjectBinderHelpers.ConvertDictToObject<NullableDateWrapper>(d);

            Assert.False(obj.Value.HasValue);
        }

        [Fact]
        public void WriteEmptyNullableDate()
        {
            // Empty
            NullableDateWrapper obj = new NullableDateWrapper();
            var d = ObjectBinderHelpers.ConvertObjectToDict(obj);

            // Nullable field shouldn't be written
            Assert.Equal(0, d.Count);
        }

        [Fact]
        public void RoundTripNullableDate()
        {
            // Empty
            var now = DateTime.UtcNow;
            NullableDateWrapper obj = new NullableDateWrapper { Value = now };
            var d = ObjectBinderHelpers.ConvertObjectToDict(obj);

            Assert.Equal(1, d.Count);
            var obj2 = ObjectBinderHelpers.ConvertDictToObject<NullableDateWrapper>(d);

            // Verify structural type equivalance. 
            var obj3 = ObjectBinderHelpers.ConvertDictToObject<DateWrapper>(d);

            Assert.Equal(obj.Value, obj2.Value);
            Assert.Equal(obj.Value, obj3.Value);
        }

        class DateWrapper
        {
            public DateTime Value { get; set; }
        }

        class NullableDateWrapper
        {
            public DateTime? Value { get; set; }
        }

        // Test reading in a DateTime that we didn't serialize.
        [Fact]
        public void ParseShortDate()
        {
            // Missing timezone information and exact tick count
            var now = DateTime.UtcNow;
            string raw = now.ToString(); // Short date format

            var d = new Dictionary<string, string>();
            d["Value"] = raw;

            DateTime val = ObjectBinderHelpers.ConvertDictToObject<DateWrapper>(d).Value;

            Assert.Equal(now.Kind, val.Kind);
            Assert.Equal(raw, val.ToString());
        }


        [Fact]
        public void ConvertStrongDictionary()
        {
            var source = new Dictionary<string, object>();
            source["A"] = 'A';
            source["One"] = 1;
            var d = ObjectBinderHelpers.ConvertObjectToDict(source);

            Assert.Equal(2, d.Count);
            Assert.Equal("A", d["A"]);
            Assert.Equal("1", d["One"]);
        }

        [Fact]
        public void TestMutate()
        {
            var obj = new StringBuilder("A");
            var source = new Dictionary<string, object>();
            source["A"] = obj;
            var d = ObjectBinderHelpers.ConvertObjectToDict(source);

            Assert.True(!object.ReferenceEquals(source, d)); // different instances
            Assert.Equal("A", d["A"]);
            
            // Now mutate.
            obj.Append("B");

            Assert.Equal("A", d["A"]);
        }

        [Fact]
        public void ConvertEnum()
        {
            var obj = new { Purchase = Fruit.Banana };
            var d = ObjectBinderHelpers.ConvertObjectToDict(obj);

            Assert.Equal(1, d.Count);
            Assert.Equal("Banana", d["Purchase"]);
        }

        // No Type converter
        public enum Fruit
        {
            Apple,
            Banana,
            Pear,
        }

        [Fact]
        public void ConvertWithTypeDescriptor()
        {
            TestEnum x = (TestEnum)ObjectBinderHelpers.BindFromStringGeneric<TestEnum>("Frown");

            // Converter overrides parse functionality
            Assert.Equal(x, TestEnum.Smile);
        }

        class TestEnumConverter : TypeConverter
        {
            public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            {
                if (sourceType == typeof(string))
                {
                    return true;
                }
                return base.CanConvertFrom(context, sourceType);
            }
            public override object ConvertFrom(ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)
            {
                string s = value as string;
                if (s != null)
                {
                    // Let's turn that frown upside down!
                    if (s == "Frown")
                    {
                        return TestEnum.Smile;
                    }
                }
                return base.ConvertFrom(context, culture, value);
            }
            public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
            {
                return destinationType == typeof(TestEnum) || base.CanConvertTo(context, destinationType);
            }
        }

        [TypeConverter(typeof(TestEnumConverter))]
        enum TestEnum
        {
            None,
            Smile,
            Frown,
        }
    }
}
