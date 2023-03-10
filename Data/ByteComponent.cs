namespace AGVServer.Data
{
	public class ByteComponent
	{
		public byte nameCode { get; set; }
		public string nameCode_string { get; set; }
		public string name { get; set; }
		public byte postfix { get; set; }

		public ushort maxVal { get; set; }
		public ByteComponent(byte nameCode, string name)
		{
			this.nameCode = nameCode;
			this.nameCode_string = BitConverter.ToString(new List<byte>() { this.nameCode }.ToArray());
			this.name = name;
			this.postfix = 0x20;
			switch (nameCode)
			{
				case 0x58:
				case 0x59:
					maxVal = 377;
					break;
				case 0x44:
					maxVal = 7999;
					break;
				default:
					maxVal = 0;
					break;
			}
		}
	}
}
