global using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WorkerHarness.Core.Tests.Helpers
{
    public class WeatherForecast
    {
        public Location? Location { get; set; }
        public int TemperatureInFahrenheit { get; set; }
        public string? Summary { get; set; }

        public static object CreateWeatherForecastObject()
        {
            var obj = new WeatherForecast()
            {
                Location = new Location()
                {
                    City = "Redmond",
                    State = "WA",
                    ZipCode = "98052"
                },
                TemperatureInFahrenheit = 73,
                Summary = "Cloudy, Rainy"
            };

            return obj;
        }
    }

    public class Location
    {
        public string? City { get; set; }
        public string? State { get; set; }
        public string? ZipCode { get; set; }
    }
}