using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using WebGhiHinh.Data;
using WebGhiHinh.Models;
using Microsoft.EntityFrameworkCore;

namespace WebGhiHinh.Controllers
{
    [Route("account")]
    public class AccountController : Controller
    {
        private readonly AppDbContext _context;

        public AccountController(AppDbContext context)
        {
            _context = context;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromForm] string username, [FromForm] string password)
        {
            // 1. Kiểm tra Database
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username && u.PasswordHash == password);

            if (user == null)
            {
                // Trả về lỗi để Blazor hiển thị
                return Redirect("/login?error=Sai tài khoản hoặc mật khẩu");
            }

            // 2. Tạo Claims (Thông tin người dùng)
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Username),
                new Claim("Id", user.Id.ToString()),
                new Claim(ClaimTypes.Role, user.Role ?? "user"),
                new Claim("FullName", user.FullName ?? "")
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true, // Nhớ đăng nhập
                ExpiresUtc = DateTime.UtcNow.AddDays(1)
            };

            // 3. GHI COOKIE VÀO TRÌNH DUYỆT (Server thực sự đăng nhập)
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            // 4. Chuyển hướng dựa trên quyền
            if (user.Role == "admin" || user.Role == "admin1")
                return Redirect("/admin");

            return Redirect("/live");
        }

        [HttpGet("logout")]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Redirect("/login");
        }
    }
}