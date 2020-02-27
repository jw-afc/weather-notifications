using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SendGrid;
using SendGrid.Helpers.Mail;
using Weather.Model;
using Microsoft.Extensions.Caching.Memory;

namespace Weather.Notifications
{
	public static class Scheduler
	{
		static IMemoryCache _cache;

		static IConfiguration _config;

		static ILogger _logger;

		static TimeZoneInfo _timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
		
		static string _alertSubject = "➹ Wind Speed Notifications";
		
		static IDictionary<string, WindCondition> _windConditions = new Dictionary<string, WindCondition>();

		[FunctionName("Scheduler")]
		public static void Run([TimerTrigger("0 */10 * * * *")]TimerInfo myTimer, ILogger log, Microsoft.Azure.WebJobs.ExecutionContext context)
		{
			_logger = log;

			_logger.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

			_config = new ConfigurationBuilder()
				.SetBasePath(context.FunctionAppDirectory)
				.AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
				.AddEnvironmentVariables()
				.Build();

			Init();

			var weather = GetWeather();
			AnalyseWeather(weather);
		}

		static string GetWeatherUrl() => $"{_config["WEATHER_URL_BASE"]}/{_config["POSTCODE"].Split(' ')[0]}".ToLower();

		static string GetWeatherUrlByType(string type) => $"{_config["WEATHER_UNLOCKED_API_BASE"]}/{type}/uk.{Regex.Replace(_config["POSTCODE"], @"\s+", string.Empty)}?app_id={_config["WEATHER_UNLOCKED_APP_ID"]}&app_key={_config["WEATHER_UNLOCKED_APP_KEY"]}";

		static Model.Weather GetWeather()
		{
			using (var client = new WebClient { Headers = new WebHeaderCollection { "accept:application/json" } })
			{
				return new Model.Weather
				{
					Current = JsonConvert.DeserializeObject<Current>(client.DownloadString(GetWeatherUrlByType("current"))),
					Forecast = JsonConvert.DeserializeObject<Forecast>(client.DownloadString(GetWeatherUrlByType("forecast")))
				};
			}
		}

		static void Init()
		{
			const string key = "ExecutionDate";

			DateTime setExecutionDate() => _cache.Set(key, TimeZoneInfo.ConvertTime(DateTime.Now, _timeZoneInfo));

			if (_cache == null)
			{
				_cache = new MemoryCache(new MemoryCacheOptions());
				setExecutionDate();
			}

			var exec = (DateTime)_cache.Get(key);
			if (exec.Date != TimeZoneInfo.ConvertTime(DateTime.Now, _timeZoneInfo).Date)
			{
				exec = setExecutionDate();

				_logger.LogInformation($"Init: {exec}");

				foreach (var windCondition in _windConditions.Where(p => p.Key == "current" || DateTime.Parse(p.Key) < exec.Date).ToList())
					_windConditions.Remove(windCondition.Key);
			}
		}
		
		static void AnalyseWeather(Model.Weather weather)
		{
			AnalyseCurrent(weather.Current);
			AnalyseForecast(weather.Forecast);
		}

		static void AnalyseCurrent(Current current)
		{
			var windCondition = AnalyseWind(current?.Wind, "current");
			if (windCondition.Send)
			{
				var sb = new StringBuilder();
				sb.Append($"<div><h3>Local Weather - {_config["POSTCODE"]}</h3></div>");
				sb.Append($"The current wind conditions exceed the stated maximum ({_config["MAXIMUM_WIND_SPEED_IN_KPH"]} kph):");
				sb.Append($"<br />&nbsp; - {TimeZoneInfo.ConvertTime(DateTime.Now, _timeZoneInfo).ToString("h:mmtt").ToLower()} - {GetWindConditions(current?.Wind)}{GetChangeIndicator(windCondition.Change, current?.Wind?.Unit ?? string.Empty)}");
				sb.Append($"<br /><br /><a href=\"{GetWeatherUrl()}\">{GetWeatherUrl()}</a>");

				SendAlert($"{_alertSubject} - Alert", sb.ToString(), true);
			}
		}

		private static void AnalyseForecast(Forecast forecast)
		{
			for (int i = 0; i < forecast.Days.Count; i++)
			{
				var alert = false;

				var day = forecast.Days[i];
				var date = day.Date;
				var forecastDay = (i == 0 ? "Today" : date.ToString("dd/MM/yyyy"));
				var dayOfWeek = date.DayOfWeek;

				var sb = new StringBuilder();
				sb.Append($"<div><h3>Local Weather - {_config["POSTCODE"]}</h3></div>");
				sb.Append($"The forecasted wind conditions for {dayOfWeek} exceed the stated maximum ({_config["MAXIMUM_WIND_SPEED_IN_KPH"]} kph): ");

				var windCondition = AnalyseForecastDay(day, date.ToString("dd/MM/yyyy"));

				if (windCondition.Send)
				{
					sb.Append($"<br /> &nbsp;- {date.ToString("dd/MM/yyyy")} - {GetWindConditions(day.Wind)}{GetChangeIndicator(windCondition.Change, day.Wind.Unit)}");
					foreach (var timeframe in day.Timeframes.Where(p => p.Wind.Speed >= int.Parse(_config["MAXIMUM_WIND_SPEED_IN_KPH"])))
					{
						windCondition = AnalyseWind(timeframe.Wind, $"{timeframe.Date.ToString("dd/MM/yyyy")} {timeframe.Time}");
						sb.Append($"<br />&nbsp;&nbsp;&nbsp; - {timeframe.Time} - {GetWindConditions(timeframe.Wind, false)}{GetChangeIndicator(windCondition.Change, timeframe.Wind.Unit)}");
					}
					alert = true;
				}

				if (alert)
				{
					sb.Append($"<br /><br /><a href=\"{GetWeatherUrl()}\">{GetWeatherUrl()}</a>");
					SendAlert($"{_alertSubject} - {dayOfWeek} ({forecastDay})", sb.ToString());
				}
			}
		}

		private static WindAlert AnalyseForecastDay(ForecastDay forecastDay, string descriptor)
		{
			return forecastDay != null ? AnalyseWind(forecastDay.Wind, descriptor) : GetWindAlert();
		}

		private static WindAlert AnalyseWind(Wind wind, string descriptor)
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

		private static WindAlert AnalyseWindConditionHistory(Wind wind, string descriptor)
		{
			var previous = _windConditions[descriptor];

			if (wind.Speed > previous.High)
				return GetWindAlert(wind, wind.Speed - previous.High, previous);

			if (wind.Speed < previous.Low)
				return GetWindAlert(wind, wind.Speed - previous.Low, previous);

			return GetWindAlert();
		}

		private static WindAlert GetWindAlert(Wind wind = null, decimal change = 0, WindCondition previous = null)
		{
			if (wind != null)
			{
				previous?.Update(wind.Speed);
				return new WindAlert(wind.Speed > int.Parse(_config["MAXIMUM_WIND_SPEED_IN_KPH"]), change);
			}
			return new WindAlert();
		}

		private static string GetChangeIndicator(decimal change, string unit)
		{
			return change != 0 ? $" ({(change < 0 ? "⇩" : "⇧")} {Math.Abs(change)} {unit})" : string.Empty;
		}

		private static string GetWindConditions(Wind wind, bool embolden = true)
		{
			return wind != null ? $"{(embolden ? "<strong>" : string.Empty)}{wind.Speed} {wind.Unit} from {wind.Direction}{(embolden ? "</strong>" : string.Empty)}" : string.Empty;
		}

		private static async void SendAlert(string subject, string content, bool important = false)
		{
			var client = new SendGridClient(_config["SENDGRID_APIKEY"]);

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

			msg.AddTo(new EmailAddress(_config["SENDGRID_RECIPIENT"]));

			var response = await client.SendEmailAsync(msg);

			_logger.LogInformation(msg.Serialize());
			_logger.LogInformation($"StatusCode: {response.StatusCode}");
		}
	}
}
