using Newtonsoft.Json;
using System.Collections.Generic;

namespace WeatherModel
{
	public class Forecast
	{
		[JsonProperty("Days")]
		public List<ForecastDay> Days { get; set; }
	}
}