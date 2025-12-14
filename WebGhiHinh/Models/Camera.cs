using System.ComponentModel.DataAnnotations;

namespace WebGhiHinh.Models
{
    public class Camera
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty; // VD: Cam_Cong_1

        [Required]
        public string RtspUrl { get; set; } = string.Empty; // Link RTSP từ Camera

        public string? WebrtcName { get; set; } // Tên dùng cho luồng WebRTC (MediaMTX)
    }
}