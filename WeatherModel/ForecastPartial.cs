using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace WeatherModel
{
	public class ForecastPartial
	{
		[JsonProperty("weather_code")]
		public int WeatherCode { get; set; }

		[JsonProperty("weather_text")]
		public string WeatherText { get; set; }

		[JsonProperty("wind")]
		public List<Wind> WindData { get; set; }

		public Wind Wind => WindData?.FirstOrDefault();
	}
}