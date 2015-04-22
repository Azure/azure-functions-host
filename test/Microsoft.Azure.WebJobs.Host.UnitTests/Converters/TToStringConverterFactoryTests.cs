// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using Microsoft.Azure.WebJobs.Host.Converters;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Converters
{
    public class TToStringConverterFactoryTests
    {
        [Fact]
        public void TryCreate_String_CanConvert()
        {
            // Act
            IConverter<string, string> converter = TToStringConverterFactory.TryCreate<string>();

            // Assert
            Assert.NotNull(converter);
            const string expected = "abc";
            string actual = converter.Convert(expected);
            Assert.Same(expected, actual);
        }

        [Fact]
        public void TryCreate_Char_CanConvert()
        {
            // Act
            IConverter<char, string> converter = TToStringConverterFactory.TryCreate<char>();

            // Assert
            Assert.NotNull(converter);
            const char value = 'a';
            string actual = converter.Convert(value);
            string expected = Char.ToString(value);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void TryCreate_Byte_CanConvert()
        {
            // Act
            IConverter<byte, string> converter = TToStringConverterFactory.TryCreate<byte>();

            // Assert
            Assert.NotNull(converter);
            const byte value = 255;
            string actual = converter.Convert(value);
            string expected = "255";
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void TryCreate_SByte_CanConvert()
        {
            // Arrange
            using (new CultureInfoContext(CreateCultureWithDifferentNegativeSign()))
            {

                // Act
                IConverter<sbyte, string> converter = TToStringConverterFactory.TryCreate<sbyte>();

                // Assert
                Assert.NotNull(converter);
                const sbyte value = -128;
                string actual = converter.Convert(value);
                string expected = "-128";
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void TryCreate_Int16_CanConvert()
        {
            // Arrange
            using (new CultureInfoContext(CreateCultureWithDifferentNegativeSign()))
            {

                // Act
                IConverter<short, string> converter = TToStringConverterFactory.TryCreate<short>();

                // Assert
                Assert.NotNull(converter);
                const short value = -32768;
                string actual = converter.Convert(value);
                string expected = "-32768";
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void TryCreate_UInt16_CanConvert()
        {
            // Act
            IConverter<ushort, string> converter = TToStringConverterFactory.TryCreate<ushort>();

            // Assert
            Assert.NotNull(converter);
            const ushort value = 65535;
            string actual = converter.Convert(value);
            string expected = "65535";
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void TryCreate_Int32_CanConvert()
        {
            // Arrange
            using (new CultureInfoContext(CreateCultureWithDifferentNegativeSign()))
            {

                // Act
                IConverter<int, string> converter = TToStringConverterFactory.TryCreate<int>();

                // Assert
                Assert.NotNull(converter);
                const int value = -2147483648;
                string actual = converter.Convert(value);
                string expected = "-2147483648";
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void TryCreate_UInt32_CanConvert()
        {
            // Act
            IConverter<uint, string> converter = TToStringConverterFactory.TryCreate<uint>();

            // Assert
            Assert.NotNull(converter);
            const uint value = 4294967295;
            string actual = converter.Convert(value);
            string expected = "4294967295";
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void TryCreate_Int64_CanConvert()
        {
            // Arrange
            using (new CultureInfoContext(CreateCultureWithDifferentNegativeSign()))
            {

                // Act
                IConverter<long, string> converter = TToStringConverterFactory.TryCreate<long>();

                // Assert
                Assert.NotNull(converter);
                const long value = -9223372036854775808;
                string actual = converter.Convert(value);
                string expected = "-9223372036854775808";
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void TryCreate_UInt64_CanConvert()
        {
            // Act
            IConverter<ulong, string> converter = TToStringConverterFactory.TryCreate<ulong>();

            // Assert
            Assert.NotNull(converter);
            const ulong value = 18446744073709551615;
            string actual = converter.Convert(value);
            string expected = "18446744073709551615";
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void TryCreate_Guid_CanConvert()
        {
            // Act
            IConverter<Guid, string> converter = TToStringConverterFactory.TryCreate<Guid>();

            // Assert
            Assert.NotNull(converter);
            Guid value = Guid.Empty;
            string actual = converter.Convert(value);
            string expected = "00000000-0000-0000-0000-000000000000";
            Assert.Equal(expected, actual);
        }

        private static CultureInfo CreateCultureWithDifferentNegativeSign()
        {
            CultureInfo cultureInfo = CultureInfo.CreateSpecificCulture("");
            cultureInfo.NumberFormat.NegativeSign = "!";
            return cultureInfo;
        }
    }
}
