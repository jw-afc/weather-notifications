using Newtonsoft.Json;
using SendGrid;
using SendGrid.Helpers.Mail;
using System;
using System.Collections.Generic;
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
		private DateTime _executionDate;
		
		private string _alertSubject = "➹ Wind Speed Notifications";
		private int _maximumWindSpeed = int.Parse(Environment.GetEnvironmentVariable("MAXIMUM_WIND_SPEED_IN_KPH"));
		private string _postcode = Environment.GetEnvironmentVariable("WEATHER2_POSTCODE");
		private string _postcodeId = Environment.GetEnvironmentVariable("WEATHER2_POSTCODE_ID");
		private string _uac = Environment.GetEnvironmentVariable("WEATHER2_UAC");

		private TimeZoneInfo _timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
		private IDictionary<string, int> _windConditions = new Dictionary<string, int>();
				
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

			_executionDate = TimeZoneInfo.ConvertTime(DateTime.Now, _timeZoneInfo);

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
				if (_executionDate.Date != TimeZoneInfo.ConvertTime(DateTime.Now, _timeZoneInfo).Date) Reset();
				
				using (var client = new WebClient())
				{
					var json = client.DownloadString($"http://www.myweather2.com/developer/forecast.ashx?uac={_uac}&output=json&query={WebUtility.UrlEncode(_postcode)}");
					WeatherRoot root = JsonConvert.DeserializeObject<WeatherRoot>(json);
					AnalyseWeather(root.Weather);
				}
			}
		}

		private void Reset()
		{
			Console.WriteLine("Reset");
			_executionDate = TimeZoneInfo.ConvertTime(DateTime.Now, _timeZoneInfo);
			_windConditions = new Dictionary<string, int>();
		}
		
		private void AnalyseWeather(Weather weather)
		{
			AnalyseCurrent(weather.Current);
			AnalyseForecast(weather.Forecasts);
		}

		private void AnalyseCurrent(Current current)
		{
			var windCondition = AnalyseWind(current?.Wind, "current");
			if (windCondition.Alert)
			{
				var sb = new StringBuilder();				
				sb.Append($"<div><h3>Local Weather - {_postcode}</h3></div>");
				sb.Append($"The current wind conditions exceed the stated maximum ({_maximumWindSpeed} kph):");
				sb.Append($"<br /> - {TimeZoneInfo.ConvertTime(DateTime.Now, _timeZoneInfo).ToString("HH:mm")} {GetWindConditions(current?.Wind)}{GetChangeIndicator(windCondition.Change, current?.Wind?.Unit ?? string.Empty)}");

				var url = $"http://www.myweather2.com/activity/current-weather.aspx?id={_postcodeId}";
				sb.Append($"<br /><br /><a href=\"{url}\">{url}</a>");
				
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

				var dayWindCondition = AnalyseForecastPartial(day, $"day{i}");
				var nightWindCondition = AnalyseForecastPartial(night, $"night{i}");

				if (dayWindCondition.Alert)
				{
					sb.Append($"<br /> - Day {GetWindConditions(day.Wind)}{GetChangeIndicator(dayWindCondition.Change, day.Wind.Unit)}");
					sb.Append($"<br /> - Night {GetWindConditions(night.Wind)}{GetChangeIndicator(nightWindCondition.Change, day.Wind.Unit)}");
					alert = true;
				}
				
				if (nightWindCondition.Alert && !alert)
				{
					sb.Append($"<br /> - Day {GetWindConditions(day.Wind)}{GetChangeIndicator(dayWindCondition.Change, day.Wind.Unit)}");
					sb.Append($"<br /> - Night {GetWindConditions(night.Wind)}{GetChangeIndicator(nightWindCondition.Change, day.Wind.Unit)}");
					alert = true;
				}

				if (alert)
				{
					var url = $"http://www.myweather2.com/activity/forecast.aspx?query={WebUtility.UrlEncode(_postcode)}&rt=postcode&id={_postcodeId}{(i > 0 ? "&sday=1" : string.Empty)}";
					sb.Append($"<br /><br /><a href=\"{url}\">{url}</a>");
					SendAlert($"{_alertSubject} - {forecastDay}", sb.ToString());
				}
			}
		}
		
		private WindCondition AnalyseForecastPartial(ForecastPartial forecastPartial, string descriptor)
		{
			return forecastPartial != null ? AnalyseWind(forecastPartial.Wind, descriptor) : new WindCondition(false, 0);
		}

		private WindCondition AnalyseWind(Wind wind, string descriptor)
		{
			if (wind != null)
			{
				if (_windConditions.ContainsKey(descriptor))
				{
					var previous = _windConditions[descriptor];
					if (wind.Speed != previous)
					{
						_windConditions[descriptor] = wind.Speed;
						return new WindCondition(wind.Speed > _maximumWindSpeed, wind.Speed - previous);
					}
				}
				else
				{
					_windConditions.Add(new KeyValuePair<string, int>(descriptor, wind.Speed));
					return new WindCondition(wind.Speed > _maximumWindSpeed, 0);
				}
			}
			return new WindCondition(false, 0);
		}
		
		private string GetChangeIndicator(int change, string unit)
		{
			if (change == 0) return string.Empty;
			return $" ({(change < 0 ? "⇩" : "⇧")} {Math.Abs(change)} {unit})";
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

		private class WindCondition
		{
			public bool Alert { get; set; }
			public int Change { get; set; }

			public WindCondition(bool alert, int change)
			{
				Alert = alert;
				Change = change;
			}
		}
	}	
}
