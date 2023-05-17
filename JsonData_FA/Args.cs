using Newtonsoft.Json;
using static DevExpress.XtraPrinting.Export.Pdf.PdfImageCache;

namespace AGVServer.Data
{
	public class Args
	{
		public DateTime start_time { get; set; }
		public DateTime end_time { get; set; }
		public string interval { get; set; }
		[JsonProperty("params")]
		public Params_FA params_FA { get; set; }
	}
}
