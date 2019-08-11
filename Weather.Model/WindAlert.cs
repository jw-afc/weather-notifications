namespace Weather.Model
{
	public class WindAlert
	{
		public bool Send { get; set; }
		public decimal Change { get; set; }

		public WindAlert(bool send = false, decimal change = 0)
		{
			Send = send;
			Change = change;
		}
	}
}
