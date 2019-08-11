using Newtonsoft.Json;
using System;

namespace Weather.Model
{
	public class Current
    {
		[JsonProperty("windspd_kmh")]
		private decimal _windSpeed { get; set; }

		[JsonProperty("winddir_compass")]
		private string _windDirection { get; set; }

		public DateTime Date => DateTime.Now;

		public Wind Wind => new Wind
		{
			Speed = _windSpeed,
			Direction = _windDirection
		};
	}
}
