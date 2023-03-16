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
		public string Status { get; set; }
		[Required]
		public ushort Priority { get; set; }
	}
}
