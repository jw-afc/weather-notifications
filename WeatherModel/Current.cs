using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace WeatherModel
{
	public class Current
    {
		[JsonProperty("humidity")]
		public int Humidity { get; set; }

		[JsonProperty("pressure")]
		public int Pressure { get; set; }

		[JsonProperty("temp")]
		public int Temperature { get; set; }

		[JsonProperty("temp_unit")]
		public string TemperatureUnit { get; set; }

		[JsonProperty("weather_code")]
		public int WeatherCode { get; set; }

		[JsonProperty("weather_text")]
		public string WeatherText { get; set; }

		[JsonProperty("wind")]
		public List<Wind> WindData { get; set; }

		public Wind Wind => WindData?.FirstOrDefault();
	}
}
