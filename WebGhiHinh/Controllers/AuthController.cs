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
                PasswordHash = request.Password,
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
        // ✅ Đã XÓA [IgnoreAntiforgery] vì Program.cs đã DisableAntiforgery()
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
                    access_token = token,
                    token = token,
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
            if (string.IsNullOrEmpty(keyStr)) keyStr = "Key_Du_Phong_Cuc_Manh_Chong_Sap_IIS_123456789_ABC";

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyStr));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new Claim("Id", user.Id.ToString()),
                new Claim("sub", user.Id.ToString()),
                new Claim("name", user.Username),
                new Claim("role", user.Role),
                new Claim("FullName", user.FullName ?? "")
            };

            var token = new JwtSecurityToken(
                // Đọc đúng từ appsettings hoặc fallback về localhost
                issuer: _config["Jwt:Issuer"] ?? "http://localhost:5000",
                audience: _config["Jwt:Audience"] ?? "http://localhost:5000",
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