using System.ComponentModel.DataAnnotations;

namespace AGVServer.Data
{
	public class MesTask
	{
		[Required]
		public string TaskNO { get; set; }
		[Required]
		public string From { get; set; }
		[Required]
		public string To { get; set; }
		[Required]
		public string SequenceNum { get; set; }
		[Required]
		public bool highOrLow { get; set; }//0:high 1:low
		[Required]
		public bool inOrOut { get; set; }//0:in 1:out
		[Required]
		public string Status { get; set; }
		[Required]
		public ushort Priority { get; set; }
	}
}
