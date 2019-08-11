namespace Weather.Model
{
	public class Wind
    {
		public decimal Speed { get; set; }

		public string Direction { get; set; }

		public string Unit => "kph";
	}
}
