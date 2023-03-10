namespace AGVServer.Data
{
	public class ByteCommand
	{
		public byte _byte { get; set; }
		public string _byte_string { get; set; }
		public string command { get; set; }
		public ByteCommand(byte _byte, string command)
		{
			this._byte = _byte;
			this._byte_string = BitConverter.ToString(new List<byte>() { this._byte }.ToArray());
			this.command = command;
		}
	}
}
