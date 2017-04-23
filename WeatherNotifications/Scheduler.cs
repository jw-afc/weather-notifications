using Newtonsoft.Json;
using SendGrid;
using SendGrid.Helpers.Mail;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using WeatherModel;

namespace WeatherNotifications
{
	public sealed class Scheduler : IDisposable
	{
		private Timer _timer;
		private int _maximumWindSpeed;
		private string _postcode;
		private string _postcodeId;

		private string _alertSubject = "➹ Wind Speed Notifications";
		private IDictionary<string, int> _weatherConditions = new Dictionary<string, int>();
		private DateTime _executionDate = DateTime.UtcNow;

		private object _lock = new object();

		public static Scheduler Instance { get; } = new Scheduler();

		static Scheduler()
		{ }

		/// <summary>
		/// Default constructor
		/// </summary>
		private Scheduler()
		{ }

		public void Start()
		{
			_timer = new Timer(int.Parse(Environment.GetEnvironmentVariable("TIMER_INTERVAL_IN_SECONDS")) * 1000);
			_timer.Elapsed += _timer_Elapsed;
			_timer.Start();

			GetWeatherForecast();
		}

		private void _timer_Elapsed(object sender, ElapsedEventArgs e)
		{
			Task.Factory.StartNew(GetWeatherForecast);
		}

		private void GetWeatherForecast()
		{
			lock (_lock)
			{
				UpdateRuntimeVariables();

				var uac = Environment.GetEnvironmentVariable("WEATHER2_UAC");

				using (var client = new WebClient())
				{
					var json = client.DownloadString($"http://www.myweather2.com/developer/forecast.ashx?uac={uac}&output=json&query={WebUtility.UrlEncode(_postcode)}");
					WeatherRoot root = JsonConvert.DeserializeObject<WeatherRoot>(json);

					AnalyseWeather(root.Weather);
				}
			}
		}

		private void Reset()
		{
			Console.WriteLine($"reset");
			_executionDate = DateTime.UtcNow;
			_weatherConditions = new Dictionary<string, int>();
		}

		private void UpdateRuntimeVariables()
		{
			_maximumWindSpeed = int.Parse(Environment.GetEnvironmentVariable("MAXIMUM_WIND_SPEED_IN_KPH"));
			_postcode = Environment.GetEnvironmentVariable("WEATHER2_POSTCODE");
			_postcodeId = Environment.GetEnvironmentVariable("WEATHER2_POSTCODE_ID");
		}

		private void AnalyseWeather(Weather weather)
		{
			if ((weather?.Forecasts?.FirstOrDefault()?.Date.Date ?? DateTime.Now) != _executionDate.Date) Reset();

			AnalyseCurrent(weather.Current);
			AnalyseForecast(weather.Forecasts);
		}

		private void AnalyseCurrent(Current current)
		{
			var result = AnalyseWind(current?.Wind, "current");
			if (result.Item1)
			{
				var sb = new StringBuilder();				
				sb.Append($"<div><h3>Local Weather - {_postcode}</h3></div>");
				sb.Append($"The current wind conditions exceed the stated maximum ({_maximumWindSpeed} kph):");
				sb.Append("<br />");
				sb.Append($" - {TimeZoneInfo.ConvertTime(DateTime.Now, TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time")).ToString("HH:mm")} {GetWindConditions(current?.Wind)}{GetChangeIndicator(result.Item2, current?.Wind?.Unit ?? string.Empty)}");
				sb.Append("<br />");
				sb.Append("<br />");
				sb.Append($"http://www.myweather2.com/activity/current-weather.aspx?id={_postcodeId}");
				sb.Append("</div>");

				SendAlert($"{_alertSubject} - Current", sb.ToString(), true);
			}
		}

		private void AnalyseForecast(List<Forecast> forecasts)
		{
			for (int i = 0; i < forecasts.Count; i++)
			{
				var forecastDay = (i == 0 ? "Today" : "Tomorrow");

				var alert = false;

				var sb = new StringBuilder();
				sb.Append($"<div><h3>Local Weather - {_postcode} - {forecasts[i].Date.ToString("dd/MM/yyyy")}</h3></div>");
				sb.Append($"The forecasted wind conditions for {forecastDay.ToLower()} exceed the stated maximum ({_maximumWindSpeed} kph): ");

				var day = forecasts[i].Day;
				var night = forecasts[i].Night;

				var dayResult = AnalyseForecastPartial(day, $"day{i}");
				var nightResult = AnalyseForecastPartial(night, $"night{i}");

				if (dayResult.Item1)
				{
					sb.Append("<br />");
					sb.Append($" - Day {GetWindConditions(day.Wind)}{GetChangeIndicator(dayResult.Item2, day.Wind.Unit)}");
					sb.Append("<br />");
					sb.Append($" - Night {GetWindConditions(night.Wind)}{GetChangeIndicator(nightResult.Item2, day.Wind.Unit)}");
					alert = true;
				}
				
				if (nightResult.Item1 && !alert)
				{
					sb.Append("<br />");
					sb.Append($" - Day {GetWindConditions(day.Wind)}{GetChangeIndicator(dayResult.Item2, day.Wind.Unit)}");
					sb.Append("<br />");
					sb.Append($" - Night {GetWindConditions(night.Wind)}{GetChangeIndicator(nightResult.Item2, day.Wind.Unit)}");
					alert = true;
				}

				if (alert)
				{
					sb.Append("<br />");
					sb.Append("<br />");
					sb.Append($"http://www.myweather2.com/activity/forecast.aspx?query={WebUtility.UrlEncode(_postcode)}&rt=postcode&id={_postcodeId}{(i > 0 ? "&sday=1" : string.Empty)}");
					SendAlert($"{_alertSubject} - {forecastDay}", sb.ToString());
				}
			}
		}

		private string GetChangeIndicator(int change, string unit)
		{
			if (change == 0) return string.Empty;
			return $" ({(change < 0 ? "⇩" : "⇧")} {Math.Abs(change)} {unit})"; 
		}

		private Tuple<bool, int> AnalyseForecastPartial(ForecastPartial forecastPartial, string descriptor)
		{
			return forecastPartial != null ? AnalyseWind(forecastPartial.Wind, descriptor) : new Tuple<bool, int>(false, 0);
		}

		private Tuple<bool, int> AnalyseWind(Wind wind, string descriptor)
		{
			if (wind != null)
			{
				if (_weatherConditions.ContainsKey(descriptor))
				{
					var previous = _weatherConditions[descriptor];
					if (wind.Speed != previous)
					{
						_weatherConditions[descriptor] = wind.Speed;
						return new Tuple<bool, int>(wind.Speed > _maximumWindSpeed, wind.Speed - previous);
					}
				}
				else
				{
					_weatherConditions.Add(new KeyValuePair<string, int>(descriptor, wind.Speed));
					return new Tuple<bool, int>(wind.Speed > _maximumWindSpeed, 0);
				}
			}
			return new Tuple<bool, int>(false, 0);
		}

		private string GetWindConditions(Wind wind)
		{
			return wind != null ? $"<strong>{wind.Speed} {wind.Unit} from {wind.Direction}</strong>" : string.Empty;
		}

		private async void SendAlert(string subject, string content, bool important = false)
		{
			var client = new SendGridClient(Environment.GetEnvironmentVariable("SENDGRID_APIKEY"));

			// Send a Single Email using the Mail Helper with convenience methods and initialized SendGridMessage object
			var msg = new SendGridMessage()
			{
				From = new EmailAddress("no-reply@sendgrid.com"),
				Subject = subject,
				HtmlContent = content
			};

			if (important)
			{
				msg.Headers = new Dictionary<string, string>
				{
					{ "Priority", "Urgent" },
					{ "Importance", "high" },
					{ "X-Priority", "1" },
					{ "X-MSMail-Priority", "high" }
				};
			}

			msg.AddTo(new EmailAddress(Environment.GetEnvironmentVariable("RECIPIENT")));

			var response = await client.SendEmailAsync(msg);

			Console.WriteLine(msg.Serialize());
			Console.WriteLine(response.StatusCode);
		}

		public void Dispose()
		{
			_timer?.Dispose();
		}
	}
}
