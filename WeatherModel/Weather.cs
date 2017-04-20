using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace WeatherModel
{
	public class Weather
	{
		[JsonProperty("curren_weather")]
		public List<Current> CurrentData { get; set; }

		[JsonProperty("forecast")]
		public List<Forecast> Forecasts { get; set; }

		public Current Current => CurrentData?.FirstOrDefault();
	}
}
