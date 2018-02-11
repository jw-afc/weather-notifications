using Newtonsoft.Json;
using System;
using System.Globalization;

namespace WeatherModel
{
	public class Timeframe
	{
		[JsonProperty("date")]
		private string _date { get; set; }

		[JsonProperty("time")]
		private int _time { get; set; }

		[JsonProperty("windspd_kmh")]
		private decimal _windSpeed { get; set; }

		[JsonProperty("winddir_compass")]
		private string _windDirection { get; set; }

		public DateTime Date => DateTime.Parse(_date, new CultureInfo("en-GB").DateTimeFormat);
		
		public string Time => ParseTime(_time.ToString().PadLeft(4, '0'), Date).ToString("htt").ToLower();

		public Wind Wind => new Wind
		{
			Speed = _windSpeed,
			Direction = _windDirection
		};

		private DateTime ParseTime(string time, DateTime date)
		{
			string hour = time.Substring(0, 2);
			int hourInt = int.Parse(hour);
			if (hourInt >= 24)
			{
				throw new ArgumentOutOfRangeException("Invalid hour");
			}

			string minute = time.Substring(2, 2);
			int minuteInt = int.Parse(minute);
			if (minuteInt >= 60)
			{
				throw new ArgumentOutOfRangeException("Invalid minute");
			}

			return new DateTime(date.Year, date.Month, date.Day, hourInt, minuteInt, 0);
		}
	}
}