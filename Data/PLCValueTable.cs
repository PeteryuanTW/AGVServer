namespace AGVServer.Data
{
	public class PLCValueTable
	{
		public ushort mxIndex { get; set; }
		public ushort modbusIndex { get; set; }

		public bool modbusValue { get; set; }

		public bool mxValue { get; set; }
		public bool mxSuccessWrite { get; set; }

		public bool mxSuccessRead { get; set; }
	}
}
