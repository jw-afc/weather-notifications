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
		private int _maximumWindSpeed;
		private string _postcode;
		private string _postcodeId;

		private string _alertSubject = "➹ Wind Speed Notifications";
		private IDictionary<string, int> _weatherConditions = new Dictionary<string, int>();
		private DateTime _executionDate = DateTime.Now;

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
				if (DateTime.Now.Date != _executionDate.Date) Reset();

				UpdateRuntimeVariables();

				var uac = Environment.GetEnvironmentVariable("WEATHER2_UAC");

				using (var client = new WebClient())
				{
					var json = client.DownloadString($"http://www.myweather2.com/developer/forecast.ashx?uac={uac}&output=json&query={_postcode}");
					WeatherRoot root = JsonConvert.DeserializeObject<WeatherRoot>(json);

					AnalyseWeather(root.Weather);
				}
			}
		}

		private void Reset()
		{
			Console.WriteLine("reset");
			_executionDate = DateTime.Now;
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
			AnalyseCurrent(weather.Current);
			AnalyseForecast(weather.Forecasts);
		}

		private void AnalyseCurrent(Current current)
		{
			if (AnalyseWind(current?.Wind, "current"))
			{
				var sb = new StringBuilder();
				sb.Append($"The current wind conditions exceed the stated maximum ({_maximumWindSpeed} kph):");
				sb.Append("<br />");
				sb.Append($" - Now {GetWindConditions(current?.Wind)}");
				sb.Append("<br />");
				sb.Append("<br />");
				sb.Append($"http://www.myweather2.com/activity/current-weather.aspx?id={_postcodeId}");

				SendAlert($"{_alertSubject} - Now", sb.ToString(), true);
			}
		}

		private void AnalyseForecast(List<Forecast> forecasts)
		{
			for (int i = 0; i < forecasts.Count; i++)
			{
				var forecastDay = (i == 0 ? "Today" : "Tomorrow");

				var alert = false;

				var sb = new StringBuilder();
				sb.Append($"The forecasted wind conditions for {forecastDay.ToLower()} exceed the stated maximum ({_maximumWindSpeed} kph): ");

				var day = forecasts[i].Day;
				var night = forecasts[i].Night;

				if (AnalyseForecastPartial(day, $"day{i}"))
				{
					sb.Append("<br />");
					sb.Append($" - Day {GetWindConditions(day.Wind)}");
					sb.Append("<br />");
					sb.Append($" - Night {GetWindConditions(night.Wind)}");
					alert = true;
				}
				
				if (AnalyseForecastPartial(night, $"night{i}") && !alert)
				{
					sb.Append("<br />");
					sb.Append($" - Day {GetWindConditions(day.Wind)}");
					sb.Append("<br />");
					sb.Append($" - Night {GetWindConditions(night.Wind)}");
					alert = true;
				}

				if (alert)
				{
					sb.Append("<br />");
					sb.Append("<br />");
					sb.Append($"http://www.myweather2.com/activity/forecast.aspx?query={_postcode}&rt=postcode&id={_postcodeId}{(i > 0 ? "&sday=1" : string.Empty)}");
					SendAlert($"{_alertSubject} - {forecastDay}", sb.ToString());
				}
			}
		}

		private bool AnalyseForecastPartial(ForecastPartial forecastPartial, string descriptor)
		{
			return forecastPartial != null ? AnalyseWind(forecastPartial.Wind, descriptor) : false;
		}

		private bool AnalyseWind(Wind wind, string descriptor)
		{
			if (wind != null)
			{
				if (_weatherConditions.ContainsKey(descriptor))
				{
					var previous = _weatherConditions[descriptor];
					if (wind.Speed > previous) return wind.Speed > _maximumWindSpeed;
				}
				else
				{
					_weatherConditions.Add(new KeyValuePair<string, int>(descriptor, wind.Speed));
					return wind.Speed > _maximumWindSpeed;
				}
			}
			return false;
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
