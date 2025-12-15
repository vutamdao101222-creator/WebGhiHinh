namespace WebGhiHinh.Models
{
    public class ScanRequest
    {
        public string QrCode { get; set; } = "";
        public string RtspUrl { get; set; } = "";
        public string StationName { get; set; } = "";
        public ScanSourceMode Mode { get; set; } = ScanSourceMode.BarcodeGun;
    }

 
}
