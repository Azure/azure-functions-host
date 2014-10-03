// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Converters
{
    public class StringToTConverterFactoryTests
    {
        [Fact]
        public void TryCreate_String_CanConvert()
        {
            // Act
            IConverter<string, string> converter = StringToTConverterFactory.Instance.TryCreate<string>();

            // Assert
            Assert.NotNull(converter);
            const string expected = "abc";
            string actual = converter.Convert(expected);
            Assert.Same(expected, actual);
        }

        [Fact]
        public void TryCreate_TypeWithTryParseMethod_CanConvert()
        {
            // Act
            IConverter<string, ClassWithTryParseMethod> converter =
                StringToTConverterFactory.Instance.TryCreate<ClassWithTryParseMethod>();

            // Assert
            Assert.NotNull(converter);
            const string expected = "abc";
            ClassWithTryParseMethod actual = converter.Convert(expected);
            Assert.NotNull(actual);
            Assert.Same(expected, actual.Value);
        }

        [Fact]
        public void TryCreate_Byte_CanConvert()
        {
            // Act
            IConverter<string, byte> converter = StringToTConverterFactory.Instance.TryCreate<byte>();

            // Assert
            Assert.NotNull(converter);
            byte actual = converter.Convert("255");
            const byte expected = 255;
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void TryCreate_Byte_CannotConvertWhenWhitespaceIsPresent()
        {
            // Act
            IConverter<string, byte> converter = StringToTConverterFactory.Instance.TryCreate<byte>();

            // Assert
            Assert.NotNull(converter);
            ExceptionAssert.ThrowsFormat(() => converter.Convert(" 255 "), "Input string was not in a correct format.");
        }

        [Fact]
        public void TryCreate_SByte_CanConvert()
        {
            // Arrange
            using (new CultureInfoContext(CreateCultureWithDifferentNegativeSign()))
            {
                // Act
                IConverter<string, sbyte> converter = StringToTConverterFactory.Instance.TryCreate<sbyte>();

                // Assert
                Assert.NotNull(converter);
                sbyte actual = converter.Convert("-128");
                const sbyte expected = -128;
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void TryCreate_SByte_CannotConvertWhenWhitespaceIsPresent()
        {
            // Act
            IConverter<string, sbyte> converter = StringToTConverterFactory.Instance.TryCreate<sbyte>();

            // Assert
            Assert.NotNull(converter);
            ExceptionAssert.ThrowsFormat(() => converter.Convert(" 127 "), "Input string was not in a correct format.");
        }

        [Fact]
        public void TryCreate_Int16_CanConvert()
        {
            // Arrange
            using (new CultureInfoContext(CreateCultureWithDifferentNegativeSign()))
            {
                // Act
                IConverter<string, short> converter = StringToTConverterFactory.Instance.TryCreate<short>();

                // Assert
                Assert.NotNull(converter);
                short actual = converter.Convert("-32768");
                const short expected = -32768;
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void TryCreate_Int16_CannotConvertWhenWhitespaceIsPresent()
        {
            // Act
            IConverter<string, short> converter = StringToTConverterFactory.Instance.TryCreate<short>();

            // Assert
            Assert.NotNull(converter);
            ExceptionAssert.ThrowsFormat(() => converter.Convert(" 32767 "),
                "Input string was not in a correct format.");
        }

        [Fact]
        public void TryCreate_UInt16_CanConvert()
        {
            // Arrange
            using (new CultureInfoContext(CreateCultureWithDifferentNegativeSign()))
            {
                // Act
                IConverter<string, ushort> converter = StringToTConverterFactory.Instance.TryCreate<ushort>();

                // Assert
                Assert.NotNull(converter);
                ushort actual = converter.Convert("65535");
                const ushort expected = 65535;
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void TryCreate_UInt16_CannotConvertWhenWhitespaceIsPresent()
        {
            // Act
            IConverter<string, ushort> converter = StringToTConverterFactory.Instance.TryCreate<ushort>();

            // Assert
            Assert.NotNull(converter);
            ExceptionAssert.ThrowsFormat(() => converter.Convert(" 65535 "),
                "Input string was not in a correct format.");
        }

        [Fact]
        public void TryCreate_Int32_CanConvert()
        {
            // Arrange
            using (new CultureInfoContext(CreateCultureWithDifferentNegativeSign()))
            {
                // Act
                IConverter<string, int> converter = StringToTConverterFactory.Instance.TryCreate<int>();

                // Assert
                Assert.NotNull(converter);
                int actual = converter.Convert("-2147483648");
                const int expected = -2147483648;
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void TryCreate_Int32_CannotConvertWhenWhitespaceIsPresent()
        {
            // Act
            IConverter<string, int> converter = StringToTConverterFactory.Instance.TryCreate<int>();

            // Assert
            Assert.NotNull(converter);
            ExceptionAssert.ThrowsFormat(() => converter.Convert(" 2147483647 "),
                "Input string was not in a correct format.");
        }

        [Fact]
        public void TryCreate_UInt32_CanConvert()
        {
            // Arrange
            using (new CultureInfoContext(CreateCultureWithDifferentNegativeSign()))
            {
                // Act
                IConverter<string, uint> converter = StringToTConverterFactory.Instance.TryCreate<uint>();

                // Assert
                Assert.NotNull(converter);
                uint actual = converter.Convert("4294967295");
                const uint expected = 4294967295;
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void TryCreate_UInt32_CannotConvertWhenWhitespaceIsPresent()
        {
            // Act
            IConverter<string, uint> converter = StringToTConverterFactory.Instance.TryCreate<uint>();

            // Assert
            Assert.NotNull(converter);
            ExceptionAssert.ThrowsFormat(() => converter.Convert(" 4294967295 "),
                "Input string was not in a correct format.");
        }

        [Fact]
        public void TryCreate_Int64_CanConvert()
        {
            // Arrange
            using (new CultureInfoContext(CreateCultureWithDifferentNegativeSign()))
            {
                // Act
                IConverter<string, long> converter = StringToTConverterFactory.Instance.TryCreate<long>();

                // Assert
                Assert.NotNull(converter);
                long actual = converter.Convert("-9223372036854775808");
                const long expected = -9223372036854775808;
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void TryCreate_Int64_CannotConvertWhenWhitespaceIsPresent()
        {
            // Act
            IConverter<string, long> converter = StringToTConverterFactory.Instance.TryCreate<long>();

            // Assert
            Assert.NotNull(converter);
            ExceptionAssert.ThrowsFormat(() => converter.Convert(" 9223372036854775807 "),
                "Input string was not in a correct format.");
        }

        [Fact]
        public void TryCreate_UInt64_CanConvert()
        {
            // Arrange
            using (new CultureInfoContext(CreateCultureWithDifferentNegativeSign()))
            {
                // Act
                IConverter<string, ulong> converter = StringToTConverterFactory.Instance.TryCreate<ulong>();

                // Assert
                Assert.NotNull(converter);
                ulong actual = converter.Convert("18446744073709551615");
                const ulong expected = 18446744073709551615;
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void TryCreate_UInt64_CannotConvertWhenWhitespaceIsPresent()
        {
            // Act
            IConverter<string, ulong> converter = StringToTConverterFactory.Instance.TryCreate<ulong>();

            // Assert
            Assert.NotNull(converter);
            ExceptionAssert.ThrowsFormat(() => converter.Convert(" 18446744073709551615 "),
                "Input string was not in a correct format.");
        }

        [Fact]
        public void TryCreate_Single_CanConvert()
        {
            // Arrange
            using (new CultureInfoContext(CreateCultureWithDifferentNegativeSign()))
            {
                // Act
                IConverter<string, float> converter = StringToTConverterFactory.Instance.TryCreate<float>();

                // Assert
                Assert.NotNull(converter);
                float actual = converter.Convert("-3.40282e+038");
                const float expected = -3.40282e+038f;
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void TryCreate_Single_CannotConvertWhenWhitespaceIsPresent()
        {
            // Act
            IConverter<string, float> converter = StringToTConverterFactory.Instance.TryCreate<float>();

            // Assert
            Assert.NotNull(converter);
            ExceptionAssert.ThrowsFormat(() => converter.Convert(" 3.40282e+038 "),
                "Input string was not in a correct format.");
        }

        [Fact]
        public void TryCreate_Double_CanConvert()
        {
            // Arrange
            using (new CultureInfoContext(CreateCultureWithDifferentNegativeSign()))
            {
                // Act
                IConverter<string, double> converter = StringToTConverterFactory.Instance.TryCreate<double>();

                // Assert
                Assert.NotNull(converter);
                double actual = converter.Convert("-1.79769e+308");
                const double expected = -1.79769e+308;
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void TryCreate_Double_CannotConvertWhenWhitespaceIsPresent()
        {
            // Act
            IConverter<string, double> converter = StringToTConverterFactory.Instance.TryCreate<double>();

            // Assert
            Assert.NotNull(converter);
            ExceptionAssert.ThrowsFormat(() => converter.Convert(" 1.79769e+308 "),
                "Input string was not in a correct format.");
        }

        [Fact]
        public void TryCreate_Decimal_CanConvert()
        {
            // Arrange
            using (new CultureInfoContext(CreateCultureWithDifferentNegativeSign()))
            {
                // Act
                IConverter<string, decimal> converter = StringToTConverterFactory.Instance.TryCreate<decimal>();

                // Assert
                Assert.NotNull(converter);
                decimal actual = converter.Convert("-79228162514264337593543950335");
                const decimal expected = -79228162514264337593543950335m;
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void TryCreate_Decimal_CanConvertWhenDecimalIsPresent()
        {
            // Arrange
            using (new CultureInfoContext(CreateCultureWithDifferentNegativeSign()))
            {
                // Act
                IConverter<string, decimal> converter = StringToTConverterFactory.Instance.TryCreate<decimal>();

                // Assert
                Assert.NotNull(converter);
                decimal actual = converter.Convert("3.14");
                const decimal expected = 3.14m;
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void TryCreate_Decimal_CannotConvertWhenWhitespaceIsPresent()
        {
            // Act
            IConverter<string, decimal> converter = StringToTConverterFactory.Instance.TryCreate<decimal>();

            // Assert
            Assert.NotNull(converter);
            ExceptionAssert.ThrowsFormat(() => converter.Convert(" 79228162514264337593543950335 "),
                "Input string was not in a correct format.");
        }

        [Fact]
        public void TryCreate_BigInteger_CanConvert()
        {
            // Arrange
            using (new CultureInfoContext(CreateCultureWithDifferentNegativeSign()))
            {
                // Act
                IConverter<string, BigInteger> converter = StringToTConverterFactory.Instance.TryCreate<BigInteger>();

                // Assert
                Assert.NotNull(converter);
                BigInteger actual = converter.Convert("-18446744073709551615");
                BigInteger expected = new BigInteger(18446744073709551615) * BigInteger.MinusOne;
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void TryCreate_BigInteger_CannotConvertWhenWhitespaceIsPresent()
        {
            // Act
            IConverter<string, BigInteger> converter = StringToTConverterFactory.Instance.TryCreate<BigInteger>();

            // Assert
            Assert.NotNull(converter);
            ExceptionAssert.ThrowsFormat(() => converter.Convert(" 18446744073709551616 "),
                "The value could not be parsed.");
        }

        [Fact]
        public void TryCreate_Guid_CanConvert()
        {
            // Arrange
            using (new CultureInfoContext(CreateCultureWithDifferentNegativeSign()))
            {
                // Act
                IConverter<string, Guid> converter = StringToTConverterFactory.Instance.TryCreate<Guid>();

                // Assert
                Assert.NotNull(converter);
                Guid actual = converter.Convert("00000000-0000-0000-0000-000000000000");
                Guid expected = Guid.Empty;
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void TryCreate_Guid_CannotConvertWhenBracesArePresent()
        {
            // Act
            IConverter<string, Guid> converter = StringToTConverterFactory.Instance.TryCreate<Guid>();

            // Assert
            Assert.NotNull(converter);
            ExceptionAssert.ThrowsFormat(() => converter.Convert("{00000000-0000-0000-0000-000000000000}"),
                "Unrecognized Guid format.");
        }

        [Fact]
        public void TryCreate_DateTime_CanConvert()
        {
            // Act
            IConverter<string, DateTime> converter = StringToTConverterFactory.Instance.TryCreate<DateTime>();

            // Assert
            Assert.NotNull(converter);
            DateTime actual = converter.Convert("0001-01-01T00:00:00.0000000Z");
            DateTime expected = DateTime.MinValue;
            Assert.Equal(expected, actual);
            Assert.Equal(DateTimeKind.Utc, actual.Kind);
        }

        [Fact]
        public void TryCreate_DateTime_ConvertAssumesUtc()
        {
            // Act
            IConverter<string, DateTime> converter = StringToTConverterFactory.Instance.TryCreate<DateTime>();

            // Assert
            Assert.NotNull(converter);
            DateTime actual = converter.Convert("0001-01-01T00:00:00.0000000");
            DateTime expected = DateTime.MinValue;
            Assert.Equal(expected, actual);
            Assert.Equal(DateTimeKind.Utc, actual.Kind);
        }

        [Fact]
        public void TryCreate_DateTime_CannotConvertWhenWhitespaceIsPresent()
        {
            // Act
            IConverter<string, DateTime> converter = StringToTConverterFactory.Instance.TryCreate<DateTime>();

            // Assert
            Assert.NotNull(converter);
            ExceptionAssert.ThrowsFormat(() => converter.Convert(" 0001-01-01T00:00:00.0000000 "),
                "String was not recognized as a valid DateTime.");
        }

        [Fact]
        public void TryCreate_DateTimeOffset_CanConvert()
        {
            // Act
            IConverter<string, DateTimeOffset> converter = StringToTConverterFactory.Instance.TryCreate<DateTimeOffset>();

            // Assert
            Assert.NotNull(converter);
            DateTimeOffset actual = converter.Convert("0001-01-01T00:00:00.0000000-10:00");
            DateTimeOffset expected = new DateTimeOffset(1, 1, 1, 0, 0, 0, TimeSpan.FromHours(-10));
            Assert.Equal(expected, actual);
            Assert.Equal(expected.Offset, actual.Offset);
        }

        [Fact]
        public void TryCreate_DateTimeOffset_ConvertAssumesUtc()
        {
            // Act
            IConverter<string, DateTimeOffset> converter = StringToTConverterFactory.Instance.TryCreate<DateTimeOffset>();

            // Assert
            Assert.NotNull(converter);
            DateTimeOffset actual = converter.Convert("0001-01-01T00:00:00.0000000");
            DateTimeOffset expected = new DateTimeOffset(1, 1, 1, 0, 0, 0, TimeSpan.FromHours(0));
            Assert.Equal(expected, actual);
            Assert.Equal(expected.Offset, actual.Offset);
        }

        [Fact]
        public void TryCreate_DateTimeOffset_CannotConvertWhenWhitespaceIsPresent()
        {
            // Act
            IConverter<string, DateTimeOffset> converter =
                StringToTConverterFactory.Instance.TryCreate<DateTimeOffset>();

            // Assert
            Assert.NotNull(converter);
            ExceptionAssert.ThrowsFormat(() => converter.Convert(" 0001-01-01T00:00:00.0000000-10:00 "),
                "String was not recognized as a valid DateTime.");
        }

        [Fact]
        public void TryCreate_TimeSpan_CanConvert()
        {
            // Arrange
            using (new CultureInfoContext(CreateCultureWithDifferentDecimalSeparator()))
            {
                // Act
                IConverter<string, TimeSpan> converter = StringToTConverterFactory.Instance.TryCreate<TimeSpan>();

                // Assert
                Assert.NotNull(converter);
                TimeSpan actual = converter.Convert("-10:00:00.123");
                TimeSpan expected = new TimeSpan(0, -10, 0, 0, -123);
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void TryCreate_TimeSpan_CannotConvertWhenUsingCurrentCultureDecimalSeparator()
        {
            // Arrange
            using (new CultureInfoContext(CreateCultureWithDifferentDecimalSeparator()))
            {
                // Act
                IConverter<string, TimeSpan> converter = StringToTConverterFactory.Instance.TryCreate<TimeSpan>();

                // Assert
                Assert.NotNull(converter);
                ExceptionAssert.ThrowsFormat(() => converter.Convert("-10:00:00,123"),
                    "String was not recognized as a valid TimeSpan.");
            }
        }

        [Fact]
        public void TryCreate_TypeWithTypeConverter_CanConvert()
        {
            // Act
            IConverter<string, ClassWithTypeConverter> converter =
                StringToTConverterFactory.Instance.TryCreate<ClassWithTypeConverter>();

            // Assert
            Assert.NotNull(converter);
            const string expected = "abc";
            ClassWithTypeConverter actual = converter.Convert(expected);
            Assert.NotNull(actual);
            Assert.Same(expected, actual.Value);
        }

        // Enums are automatically supported by virtue of including TypeConverter support.
        [Fact]
        public void TryCreate_Enum_CanConvert()
        {
            // Act
            IConverter<string, SimpleEnum> converter = StringToTConverterFactory.Instance.TryCreate<SimpleEnum>();

            // Assert
            Assert.NotNull(converter);
            SimpleEnum actual = converter.Convert("Bar");
            Assert.Equal(SimpleEnum.Bar, actual);
        }

        private static CultureInfo CreateCultureWithDifferentDecimalSeparator()
        {
            return CultureInfo.CreateSpecificCulture("fr-FR");
        }

        private static CultureInfo CreateCultureWithDifferentNegativeSign()
        {
            CultureInfo cultureInfo = CultureInfo.CreateSpecificCulture("");
            cultureInfo.NumberFormat.NegativeSign = "!";
            return cultureInfo;
        }

        private class ClassWithTryParseMethod
        {
            public string Value { get; set; }

            public static bool TryParse(string input, out ClassWithTryParseMethod result)
            {
                result = new ClassWithTryParseMethod { Value = input };
                return true;
            }
        }

        [TypeConverter(typeof(CustomTypeConverter))]
        private class ClassWithTypeConverter
        {
            public string Value { get; set; }
        }

        private class CustomTypeConverter : TypeConverter
        {
            public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            {
                return sourceType == typeof(string);
            }

            public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
            {
                string stringValue = (string)value;

                return new ClassWithTypeConverter { Value = stringValue };
            }
        }

        private enum SimpleEnum
        {
            Foo,
            Bar
        }
    }
}
