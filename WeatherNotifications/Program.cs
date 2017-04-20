using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WeatherNotifications
{
	class Program
	{
		static void Main(string[] args)
		{
			Scheduler.Instance.Start();

			Console.WriteLine("Running. Press any key to stop...");
			Console.ReadLine();
		}
	}
}
