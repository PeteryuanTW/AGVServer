using AGVServer.EFModels;

namespace AGVServer.Data
{
	public class GroupClass
	{
		public string name { get; set; } = null!;
		public bool occupied { get; set; }

		public List<string> elementList { get; set; } = null!;
		public GroupClass(GroupConfig groupConfig)
		{
			name = groupConfig.GroupName;
			occupied = groupConfig.Occupied;
			elementList = groupConfig.Elements.Split(",").ToList();
		}
	}
}
