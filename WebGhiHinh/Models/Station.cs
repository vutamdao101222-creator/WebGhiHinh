using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WebGhiHinh.Models;

namespace WebGhiHinh.Models
{
    public class Station
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty; // VD: TRẠM 1

        // Liên kết: Trạm này đang gắn với Camera nào?
        public int? CameraId { get; set; }
        [ForeignKey("CameraId")]
        public Camera? Camera { get; set; }

        // Liên kết: Ai đang ngồi trạm này? (Có thể null nếu trống)
        public int? CurrentUserId { get; set; }
        [ForeignKey("CurrentUserId")]
        public User? CurrentUser { get; set; }
    }
}