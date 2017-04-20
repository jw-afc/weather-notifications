using Newtonsoft.Json;
using System.Collections.Generic;

namespace WeatherModel
{
	public class Wind
    {
		[JsonProperty("dir")]
		public string Direction { get; set; }

		[JsonProperty("dir_degree")]
		public int DirectionDegree { get; set; }

		[JsonProperty("speed")]
		public int Speed { get; set; }

		[JsonProperty("wind_unit")]
		public string Unit { get; set; }
	}
}
