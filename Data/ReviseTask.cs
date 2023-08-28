namespace AGVServer.Data
{
    public class ReviseTask
    {
        public string OriginalMesTaskNo { get; set; }
        public string NewTaskMesNo { get; set; }
        //public string GetFromMesTime { get; set; }
        public string ToStation { get; set; }
        public bool AmrtoLoaderHighOrLow { get; set; }
        public string Barcode { get; set; }

    }
}
