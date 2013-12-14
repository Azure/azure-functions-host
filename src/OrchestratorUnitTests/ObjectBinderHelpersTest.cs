using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Jobs;


namespace Microsoft.WindowsAzure.JobsUnitTests
{
    [TestClass]
    public class ObjectBinderHelpersTest
    {
        [TestMethod]
        public void ConvertAnonymousType()
        {
            var d = ObjectBinderHelpers.ConvertObjectToDict(new { A = 'A', One = 1 });

            Assert.AreEqual(2, d.Count);
            Assert.AreEqual("A", d["A"]);
            Assert.AreEqual("1", d["One"]);
        }

        [TestMethod]
        public void RoundTripDateUTc()
        {
            var now = DateTime.UtcNow; // Utc
            Assert.AreEqual(now.ToString(), now.ToUniversalTime().ToString());

            var val = RoundTripDateTime(now);

            // Full quality (including Kind)
            Assert.AreEqual(now, val);
        }
        
        [TestMethod]
        public void RoundTripDateLocal()
        {
            var now = DateTime.Now; // Local
            
            var val = RoundTripDateTime(now);
            
            // Full quality (including Kind)
            Assert.AreEqual(now.ToUniversalTime(), val);
        }
        
        private static DateTime RoundTripDateTime(DateTime now)
        {
            var d = ObjectBinderHelpers.ConvertObjectToDict(new DateWrapper { Value = now });

            Assert.AreEqual(1, d.Count);

            // Note RawString is in UTC format
            var rawVal = d["Value"];
            var dateRaw = DateTime.Parse(rawVal);
            Assert.AreEqual(now.ToUniversalTime(), dateRaw.ToUniversalTime());
            
            DateTime val = ObjectBinderHelpers.ConvertDictToObject<DateWrapper>(d).Value;

            Assert.AreEqual(val.Kind, DateTimeKind.Utc, "should have normalized Kind to be UTC");
            Assert.AreEqual(now.ToUniversalTime(), val.ToUniversalTime());

            return val;
        }

        [TestMethod]
        public void ReadEmptyNullableDate()
        {
            // Empty
            var d = new Dictionary<string, string>();
            var obj = ObjectBinderHelpers.ConvertDictToObject<NullableDateWrapper>(d);

            Assert.IsFalse(obj.Value.HasValue);
        }

        [TestMethod]
        public void WriteEmptyNullableDate()
        {
            // Empty
            NullableDateWrapper obj = new NullableDateWrapper();
            var d = ObjectBinderHelpers.ConvertObjectToDict(obj);

            Assert.AreEqual(0, d.Count, "Nullable field shouldn't be written");        
        }

        [TestMethod]
        public void RoundTripNullableDate()
        {
            // Empty
            var now = DateTime.UtcNow;
            NullableDateWrapper obj = new NullableDateWrapper { Value = now };
            var d = ObjectBinderHelpers.ConvertObjectToDict(obj);

            Assert.AreEqual(1, d.Count);
            var obj2 = ObjectBinderHelpers.ConvertDictToObject<NullableDateWrapper>(d);

            // Verify structural type equivalance. 
            var obj3 = ObjectBinderHelpers.ConvertDictToObject<DateWrapper>(d);

            Assert.AreEqual(obj.Value, obj2.Value);
            Assert.AreEqual(obj.Value, obj3.Value);
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
        [TestMethod]
        public void ParseShortDate()
        {
            // Missing timezone information and exact tick count
            var now = DateTime.UtcNow;
            string raw = now.ToString(); // Short date format

            var d = new Dictionary<string, string>();
            d["Value"] = raw;

            DateTime val = ObjectBinderHelpers.ConvertDictToObject<DateWrapper>(d).Value;

            Assert.AreEqual(now.Kind, val.Kind);
            Assert.AreEqual(raw, val.ToString());
        }


        [TestMethod]
        public void ConvertStrongDictionary()
        {
            var source = new Dictionary<string, object>();
            source["A"] = 'A';
            source["One"] = 1;
            var d = ObjectBinderHelpers.ConvertObjectToDict(source);

            Assert.AreEqual(2, d.Count);
            Assert.AreEqual("A", d["A"]);
            Assert.AreEqual("1", d["One"]);
        }

        [TestMethod]
        public void TestMutate()
        {
            var obj = new StringBuilder("A");
            var source = new Dictionary<string, object>();
            source["A"] = obj;
            var d = ObjectBinderHelpers.ConvertObjectToDict(source);

            Assert.IsTrue(!object.ReferenceEquals(source, d)); // different instances
            Assert.AreEqual("A", d["A"]);
            
            // Now mutate.
            obj.Append("B");

            Assert.AreEqual("A", d["A"]);
        }

        [TestMethod]
        public void ConvertEnum()
        {
            var obj = new { Purchase = Fruit.Banana };
            var d = ObjectBinderHelpers.ConvertObjectToDict(obj);

            Assert.AreEqual(1, d.Count);
            Assert.AreEqual("Banana", d["Purchase"]);
        }

        // No Type converter
        public enum Fruit
        {
            Apple,
            Banana,
            Pear,
        }


        [TestMethod]
        public void ConvertWithTypeDescriptor()
        {
            TestEnum x = (TestEnum)ObjectBinderHelpers.BindFromString("Frown", typeof(TestEnum));

            // Converter overrides parse functionality
            Assert.AreEqual(x, TestEnum.Smile);
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
