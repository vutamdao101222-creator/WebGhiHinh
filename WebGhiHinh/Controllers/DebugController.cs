using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Linq;

namespace WebGhiHinh.Controllers
{
    [Route("api/debug")]
    [ApiController]
    public class DebugController : ControllerBase
    {
        // API này ai cũng vào được (để test xem server có sống không)
        [HttpGet("ping")]
        public IActionResult Ping()
        {
            return Ok("Server is running!");
        }

        // API này yêu cầu Token, dùng để soi Claims
        [HttpGet("check-user")]
        [Authorize]
        public IActionResult CheckUser()
        {
            // Lấy tất cả thông tin Server đọc được từ Token
            var claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList();

            return Ok(new
            {
                Message = "Đã nhận được Token",
                IsAuthenticated = User.Identity.IsAuthenticated,
                Name = User.Identity.Name,
                // Kiểm tra xem User có quyền admin không theo cách của Server
                IsAdmin_Role = User.IsInRole("admin"),
                IsAdmin_Uppercase = User.IsInRole("Admin"),
                // Liệt kê chi tiết các quyền
                AllClaims = claims
            });
        }
    }
}