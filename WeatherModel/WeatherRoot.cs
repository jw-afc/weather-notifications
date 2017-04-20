using Newtonsoft.Json;

namespace WeatherModel
{
	public class WeatherRoot
    {
		[JsonProperty("weather")]
		public Weather Weather { get; set; }
	}
}
