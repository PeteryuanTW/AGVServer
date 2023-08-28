using AGVServer.Data;

namespace AGVServer.JsonData_FA
{
    public class FleetState
    {
        public string fleet_name { get; set; }
        public List<Robot> robots { get; set; }
    }
}
