namespace AGVServer.Data
{
	public class ByteComponent
	{
		public string name { get; set; }
		public byte nameCode { get; set; }
		public string nameCodeString { get; set; }

		public ByteComponent(string name, byte nameCode)
		{
			this.name = name;
			this.nameCode = nameCode;
			nameCodeString = BitConverter.ToString(new byte[] { nameCode }).Replace("-", " ");
		}
	}
}
