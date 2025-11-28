using Microsoft.AspNetCore.Mvc;
using WebGhiHinh.Data;
using WebGhiHinh.Models;

namespace WebGhiHinh.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SeedController : ControllerBase
    {
        private readonly AppDbContext _context;

        public SeedController(AppDbContext context)
        {
            _context = context;
        }

        [HttpPost("init")]
        public IActionResult InitData()
        {
            if (!_context.Users.Any())
            {
                _context.Users.Add(new User
                {
                    Username = "admin",
                    PasswordHash = "admin123", // Lưu ý: thực tế phải mã hóa
                    Role = "admin"
                });
                _context.SaveChanges();
                return Ok("Đã tạo user: admin / admin123");
            }
            return Ok("Dữ liệu đã có sẵn.");
        }
    }
}