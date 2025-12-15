// FILE: Models/Station.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebGhiHinh.Models
{
    public class Station
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        // ====== 2 CAMERAS ======
        public int? OverviewCameraId { get; set; }
        public int? QrCameraId { get; set; }

        [ForeignKey(nameof(OverviewCameraId))]
        public Camera? OverviewCamera { get; set; }

        [ForeignKey(nameof(QrCameraId))]
        public Camera? QrCamera { get; set; }

        // ====== CURRENT USER ======
        public int? CurrentUserId { get; set; }

        [ForeignKey(nameof(CurrentUserId))]
        public User? CurrentUser { get; set; }
    }
}
