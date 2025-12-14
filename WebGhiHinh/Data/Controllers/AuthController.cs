using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using WebGhiHinh.Data;
using WebGhiHinh.Models;

namespace WebGhiHinh.Controllers
{
    [Route("api/auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;

        public AuthController(AppDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        // POST: api/auth/register
        [HttpPost("register")]
        public async Task<IActionResult> Register(UserRegisterDto request)
        {
            if (await _context.Users.AnyAsync(u => u.Username == request.Username))
            {
                return BadRequest("Tài khoản đã tồn tại.");
            }

            var user = new User
            {
                Username = request.Username,
                PasswordHash = request.Password, // Lưu ý: Nên mã hóa mật khẩu trong thực tế
                Role = "user",
                FullName = request.FullName,
                EmployeeCode = request.EmployeeCode,
                Address = request.Address
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đăng ký thành công!" });
        }

        // POST: api/auth/login
        [HttpPost("login")]
        public async Task<IActionResult> Login(UserLoginDto request)
        {
            try
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == request.Username && u.PasswordHash == request.Password);

                if (user == null)
                {
                    return Unauthorized("Sai tên đăng nhập hoặc mật khẩu.");
                }

                string token = CreateToken(user);

                return Ok(new
                {
                    access_token = token, // Frontend của bạn đang dùng tên biến này? Hãy kiểm tra nếu code Blazor dùng 'token' hay 'access_token'
                    token = token,        // Trả về cả 2 tên cho chắc ăn (để khớp với ScanningPage.razor)
                    username = user.Username,
                    role = user.Role
                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.InnerException?.Message ?? ex.Message);
            }
        }

        private string CreateToken(User user)
        {
            var keyStr = _config["Jwt:Key"];
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyStr));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            // 👇 QUAN TRỌNG: Cấu hình các Claim
            var claims = new List<Claim>
            {
                // Key "Id" bắt buộc phải có để StationController nhận diện được người dùng
                new Claim("Id", user.Id.ToString()),

                new Claim("sub", user.Id.ToString()),    // ID người dùng (Subject - Chuẩn JWT)
                new Claim("name", user.Username),        // Tên đăng nhập
                new Claim("role", user.Role),            // Quyền (admin/user)
                new Claim("FullName", user.FullName ?? "")
            };

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.Now.AddDays(1),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }

    public class UserRegisterDto
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string FullName { get; set; }
        public string EmployeeCode { get; set; }
        public string Address { get; set; }
    }

    public class UserLoginDto
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }
}