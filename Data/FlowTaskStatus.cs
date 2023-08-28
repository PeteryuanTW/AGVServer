namespace AGVServer.Data
{
    public class FlowTaskStatus
    {
        public string flowid { get; set; }
        public int state { get; set; }
        public string amrid { get; set; }
        public int percentage { get; set; }
        public string custom_info { get; set; }
        public string status_msg { get; set; }
    }
}
