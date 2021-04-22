namespace Weather.Model
{
	public class WindCondition
	{
		public decimal High { get; set; }
		public decimal Low { get; set; }

		public WindCondition(decimal speed)
		{
			High = Low = speed;
			High = Low;
		}

		public void Update(decimal speed)
		{
			High = speed < High ? speed : High;
			Low = speed < Low ? speed : Low;
		}
	}
}
