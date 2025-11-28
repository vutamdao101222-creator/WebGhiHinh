using System.ComponentModel.DataAnnotations;

namespace WebGhiHinh.Models
{
    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Role { get; set; } = "user";

        // 👉 CÁC TRƯỜNG MỚI THÊM
        [MaxLength(100)]
        public string FullName { get; set; } = string.Empty; // Họ và tên

        [MaxLength(20)]
        public string EmployeeCode { get; set; } = string.Empty; // Mã nhân viên

        [MaxLength(200)]
        public string Address { get; set; } = string.Empty; // Địa chỉ
    }
}