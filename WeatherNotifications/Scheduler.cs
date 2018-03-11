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
		private IDictionary<string, WindCondition> _windConditions = new Dictionary<string, WindCondition>();

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
			_executionDate = TimeZoneInfo.ConvertTime(DateTime.Now.AddDays(2), _timeZoneInfo);
			
			_windConditions.Remove("current");
			foreach (var windCondition in _windConditions.Where(p => DateTime.Parse(p.Key) < _executionDate.Date).ToList())
			{
				_windConditions.Remove(windCondition.Key);
			}
		}

		private void AnalyseWeather(Weather weather)
		{
			AnalyseCurrent(weather.Current);
			AnalyseForecast(weather.Forecast);
		}

		private void AnalyseCurrent(Current current)
		{
			var windCondition = AnalyseWind(current?.Wind, "current");
			if (windCondition.Send)
			{
				var sb = new StringBuilder();
				sb.Append($"<div><h3>Local Weather - {_postcode}</h3></div>");
				sb.Append($"The current wind conditions exceed the stated maximum ({_maximumWindSpeed} kph):");
				sb.Append($"<br />&nbsp; - {TimeZoneInfo.ConvertTime(DateTime.Now, _timeZoneInfo).ToString("h:mmtt").ToLower()} - {GetWindConditions(current?.Wind)}{GetChangeIndicator(windCondition.Change, current?.Wind?.Unit ?? string.Empty)}");
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
				sb.Append($"The forecasted wind conditions for {date.DayOfWeek} exceed the stated maximum ({_maximumWindSpeed} kph): ");

				var windCondition = AnalyseForecastDay(day, date.ToString("dd/MM/yyyy"));

				if (windCondition.Send)
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

		private WindAlert AnalyseForecastDay(ForecastDay forecastDay, string descriptor)
		{
			return forecastDay != null ? AnalyseWind(forecastDay.Wind, descriptor) : new WindAlert(false, 0);
		}

		private WindAlert AnalyseWind(Wind wind, string descriptor)
		{
			if (wind != null)
			{
				if (_windConditions.ContainsKey(descriptor)) return AnalyseWindConditionHistory(wind, descriptor);
				else
				{
					_windConditions.Add(new KeyValuePair<string, WindCondition>(descriptor, new WindCondition(wind.Speed)));
					return GetWindAlert(wind);
				}
			}
			return GetWindAlert();
		}

		private WindAlert AnalyseWindConditionHistory(Wind wind, string descriptor)
		{
			var previous = _windConditions[descriptor];

			if (wind.Speed > previous.High)
				return GetWindAlert(wind, wind.Speed - previous.High, previous);

			if (wind.Speed < previous.Low)
				return GetWindAlert(wind, wind.Speed - previous.Low, previous);

			return GetWindAlert();
		}


		private WindAlert GetWindAlert(Wind wind = null, decimal change = 0, WindCondition previous = null)
		{
			if (wind != null)
			{
				previous?.Update(wind.Speed);
				return new WindAlert(wind.Speed > _maximumWindSpeed, change);
			}
			return new WindAlert();
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

		private class WindAlert
		{
			public bool Send { get; set; }
			public decimal Change { get; set; }

			public WindAlert(bool send = false, decimal change = 0)
			{
				Send = send;
				Change = change;
			}
		}

		private class WindCondition
		{
			public decimal High { get; set; }
			public decimal Low { get; set; }

			public WindCondition(decimal speed)
			{
				High = Low = speed;
			}

			public void Update(decimal speed)
			{
				High = speed > High ? speed : High;
				Low = speed < Low ? speed : Low;
			}
		}
	}
}
