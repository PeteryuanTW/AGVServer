using System.Text;

namespace AGVServer.Data
{
	public class ByteCommand
	{
		public byte[] _byte { get; set; }
		public string command { get; set; }
		public CommandType commandType { get; set; }
		public string byteString { get; set; }
		public ByteCommand(byte[] _byte, string command, CommandType commandType)
		{
			this._byte = _byte;
			byteString = BitConverter.ToString(_byte).Replace("-", " ");
			this.command = command;
			this.commandType = commandType;
		}
	}

	public enum CommandType {Read, Write, Bit, Word };
}
