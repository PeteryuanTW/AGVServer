namespace AGVServer.Data
{
	public class PLCValueTable
	{
		public ushort mxIndex { get; set; }
		public ushort modbusIndex { get; set; }

		public bool modbusValue { get; set; }

		public bool mxValue { get; set; }

		public bool updateType { get; set; }//0:modbus update to plc, 1: plc update to modbus
		public bool updateValueSuccess { get; set; }

		public bool mxSuccessRead { get; set; }
	}
}
