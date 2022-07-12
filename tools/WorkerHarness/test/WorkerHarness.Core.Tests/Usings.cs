global using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WorkerHarness.Core.Tests.Helpers
{
    public class WeatherForecast
    {
        public Location Location { get; set; } = new Location();
        public int TemperatureInFahrenheit { get; set; } = 73;
        public IList<string> Summary { get; set; } = new List<string>()
        {
            "cloudy", "rainy", "cool"
        };

        public static object CreateWeatherForecastObject()
        {
            return new WeatherForecast();
        }
    }

    public class Location
    {
        public string City { get; set; } = "Redmond";
        public string State { get; set; } = "WA";
        public string ZipCode { get; set; } = "98052";
    }
}