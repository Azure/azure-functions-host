using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RunnerInterfaces;

namespace OrchestratorUnitTests
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
