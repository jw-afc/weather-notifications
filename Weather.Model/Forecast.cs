using Newtonsoft.Json;
using System.Collections.Generic;

namespace Weather.Model
{
	public class Forecast
	{
		[JsonProperty("Days")]
		public List<ForecastDay> Days { get; set; }
	}
}