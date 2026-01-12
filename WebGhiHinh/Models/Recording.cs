using System;
using System.ComponentModel.DataAnnotations;

namespace WebGhiHinh.Models
{
    // Class đại diện cho bảng lịch sử ghi hình trong Database
    public class Recording
    {
        [Key]
        public int Id { get; set; }

        public string? StationName { get; set; }   // Tên máy (VD: May1)
        public string? UserName { get; set; }      // Tên nhân viên (VD: ctv001)
        public string? ProductCode { get; set; }   // Mã đơn hàng/QR

        public DateTime CreatedAt { get; set; } = DateTime.Now; // Thời gian ghi

        public string? VideoPath { get; set; }     // Đường dẫn file video (nếu có)
        public string? Status { get; set; }        // Trạng thái (Success/Error)
    }
}