using Newtonsoft.Json;
using SendGrid;
using SendGrid.Helpers.Mail;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
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
		private int _maximumWindSpeed = int.Parse(ConfigurationManager.AppSettings["MAXIMUM_WIND_SPEED_IN_KPH"]);
		private string _postcode = ConfigurationManager.AppSettings["POSTCODE"];
		
		private string WeatherUrl => $"{ConfigurationManager.AppSettings["WEATHER_URL_BASE"]}/{_postcode.Split(' ')[0]}".ToLower();

		private TimeZoneInfo _timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
		private IDictionary<string, decimal> _windConditions = new Dictionary<string, decimal>();
				
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
			_timer = new Timer(int.Parse(ConfigurationManager.AppSettings["TIMER_INTERVAL_IN_SECONDS"]) * 1000);
			_timer.Elapsed += _timer_Elapsed;
			_timer.Start();

			_executionDate = TimeZoneInfo.ConvertTime(DateTime.Now, _timeZoneInfo);

			Execute();
		}

		private void _timer_Elapsed(object sender, ElapsedEventArgs e)
		{
			Task.Factory.StartNew(Execute);
		}

		private void Execute()
		{
			lock (_lock)
			{
				if (_executionDate.Date != TimeZoneInfo.ConvertTime(DateTime.Now, _timeZoneInfo).Date) Reset();

				var weather = GetWeather();
				AnalyseWeather(weather);
			}
		}

		private string GetWeatherUrlByType(string type) => $"{ConfigurationManager.AppSettings["WEATHER_UNLOCKED_API_BASE"]}/{type}/uk.{Regex.Replace(_postcode, @"\s+", string.Empty)}?app_id={ConfigurationManager.AppSettings["WEATHER_UNLOCKED_APP_ID"]}&app_key={ConfigurationManager.AppSettings["WEATHER_UNLOCKED_APP_KEY"]}";

		private Weather GetWeather()
		{
			using (var client = new WebClient { Headers = new WebHeaderCollection { "accept:application/json" } })
			{
				return new Weather
				{
					Current = JsonConvert.DeserializeObject<Current>(client.DownloadString(GetWeatherUrlByType("current"))),
					Forecast = JsonConvert.DeserializeObject<Forecast>(client.DownloadString(GetWeatherUrlByType("forecast")))
				};
			}
		}

		private void Reset()
		{
			_executionDate = TimeZoneInfo.ConvertTime(DateTime.Now, _timeZoneInfo);
			_windConditions = new Dictionary<string, decimal>();
		}
		
		private void AnalyseWeather(Weather weather)
		{
			AnalyseCurrent(weather.Current);
			AnalyseForecast(weather.Forecast);
		}

		private void AnalyseCurrent(Current current)
		{
			var windCondition = AnalyseWind(current?.Wind, "current");
			if (windCondition.Alert)
			{
				var sb = new StringBuilder();
				sb.Append($"<div><h3>Local Weather - {_postcode}</h3></div>");
				sb.Append($"The current wind conditions exceed the stated maximum ({_maximumWindSpeed} kph):");
				sb.Append($"<br />&nbsp; - {TimeZoneInfo.ConvertTime(DateTime.Now, _timeZoneInfo).ToString("h:mm tt")} - {GetWindConditions(current?.Wind)}{GetChangeIndicator(windCondition.Change, current?.Wind?.Unit ?? string.Empty)}");
				sb.Append($"<br /><br /><a href=\"{WeatherUrl}\">{WeatherUrl}</a>");

				SendAlert($"{_alertSubject} - Alert", sb.ToString(), true);
			}
		}

		private void AnalyseForecast(Forecast forecast)
		{
			for (int i = 0; i < forecast.Days.Count; i++)
			{
				var alert = false;

				var day = forecast.Days[i];
				var date = day.Date;
				var forecastDay = (i == 0 ? "Today" : date.ToString("dd/MM/yyyy"));

				var sb = new StringBuilder();
				sb.Append($"<div><h3>Local Weather - {_postcode}</h3></div>");
				sb.Append($"The forecasted wind conditions exceed the stated maximum ({_maximumWindSpeed} kph): ");

				var windCondition = AnalyseForecastDay(day, date.ToString("dd/MM/yyyy"));

				if (windCondition.Alert)
				{
					sb.Append($"<br /> &nbsp;- {date.ToString("dd/MM/yyyy")} - {GetWindConditions(day.Wind)}{GetChangeIndicator(windCondition.Change, day.Wind.Unit)}");
					foreach (var timeframe in day.Timeframes.Where(p => p.Wind.Speed >= _maximumWindSpeed))
					{
						windCondition = AnalyseWind(timeframe.Wind, $"{timeframe.Date.ToString("dd/MM/yyyy")} {timeframe.Time}");
						sb.Append($"<br />&nbsp;&nbsp;&nbsp; - {timeframe.Time} - {GetWindConditions(timeframe.Wind, false)}{GetChangeIndicator(windCondition.Change, timeframe.Wind.Unit)}");
					}
					alert = true;
				}
				
				if (alert)
				{
					sb.Append($"<br /><br /><a href=\"{WeatherUrl}\">{WeatherUrl}</a>");
					SendAlert($"{_alertSubject} - {forecastDay}", sb.ToString());
				}
			}
		}
				
		private WindCondition AnalyseForecastDay(ForecastDay forecastDay, string descriptor)
		{
			return forecastDay != null ? AnalyseWind(forecastDay.Wind, descriptor) : new WindCondition(false, 0);
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
					_windConditions.Add(new KeyValuePair<string, decimal>(descriptor, wind.Speed));
					return new WindCondition(wind.Speed > _maximumWindSpeed, 0);
				}
			}
			return new WindCondition(false, 0);
		}
		
		private string GetChangeIndicator(decimal change, string unit)
		{
			if (change == 0) return string.Empty;
			return $" ({(change < 0 ? "⇩" : "⇧")} {Math.Abs(change)} {unit})";
		}

		private string GetWindConditions(Wind wind, bool embolden = true)
		{
			return wind != null ? $"{(embolden ? "<strong>" : string.Empty)}{wind.Speed} {wind.Unit} from {wind.Direction}{(embolden ? "</strong>" : string.Empty)}" : string.Empty;
		}

		private async void SendAlert(string subject, string content, bool important = false)
		{
			var client = new SendGridClient(ConfigurationManager.AppSettings["SENDGRID_APIKEY"]);

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

			msg.AddTo(new EmailAddress(ConfigurationManager.AppSettings["SENDGRID_RECIPIENT"]));

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
			public decimal Change { get; set; }

			public WindCondition(bool alert, decimal change)
			{
				Alert = alert;
				Change = change;
			}
		}
	}	
}
