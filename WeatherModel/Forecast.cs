using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace WeatherModel
{
	public class Forecast
	{
		[JsonProperty("date")]
		public DateTime Date { get; set; }

		[JsonProperty("day")]
		private List<ForecastPartial> DayData { get; set; }

		[JsonProperty("night")]
		private List<ForecastPartial> NightData { get; set; }

		[JsonProperty("day_max_temp")]
		public int MaximumTemperatureDay { get; set; }

		[JsonProperty("night_min_temp")]
		public int MinimumTemperatureNight { get; set; }

		[JsonProperty("temp_unit")]
		public string TemperatureUnit { get; set; }

		public ForecastPartial Day => DayData?.FirstOrDefault();

		public ForecastPartial Night => NightData?.FirstOrDefault();
	}
}