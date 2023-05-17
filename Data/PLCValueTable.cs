namespace AGVServer.Data
{
	public class PLCValueTable
	{
		public ushort mxIndex { get; set; }
		public ushort modbusIndex { get; set; }

		public bool vlaue { get; set; }

		public bool alive { get; set; }
	}
}
