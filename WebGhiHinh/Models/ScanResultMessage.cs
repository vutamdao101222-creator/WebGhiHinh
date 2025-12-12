namespace WebGhiHinh.Models
{
    public class ScanResultMessage
    {
        public string StationName { get; set; } = "";
        public string Code { get; set; } = "";

        // Tọa độ tương đối (0–1) trong khung hình nếu sau này anh muốn vẽ khung đúng vị trí
        public double X { get; set; }  // left
        public double Y { get; set; }  // top
        public double W { get; set; }  // width
        public double H { get; set; }  // height
    }
}
