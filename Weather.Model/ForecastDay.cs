using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Weather.Model
{
	public class ForecastDay
	{
		[JsonProperty("date")]
		private string _date;

		[JsonProperty("windspd_max_kmh")]
		private decimal _windSpeed { get; set; }

		[JsonProperty("Timeframes")]
		public List<Timeframe> Timeframes { get; set; }

		public DateTime Date => DateTime.Parse(_date, new CultureInfo("en-GB").DateTimeFormat);
		
		public Wind Wind => new Wind
		{
			Speed = _windSpeed,
			Direction = Timeframes.FirstOrDefault().Wind.Direction ?? "unknown"
		};
	}
}