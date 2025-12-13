using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebGhiHinh.Data;
using WebGhiHinh.Models;
using QRCoder;

namespace WebGhiHinh.Controllers
{
    [Route("api/users")]
    [ApiController]
    [Authorize(Roles = "admin,admin1")]
    public class UsersController : ControllerBase
    {
        private readonly AppDbContext _context;

        public UsersController(AppDbContext context)
        {
            _context = context;
        }

        // 1) LIST USERS
        [HttpGet]
        public async Task<IActionResult> GetUsers()
        {
            var users = await _context.Users
                .OrderBy(u => u.Username)
                .Select(u => new UserDto
                {
                    Id = u.Id,
                    Username = u.Username,
                    FullName = u.FullName,
                    EmployeeCode = u.EmployeeCode,
                    Address = u.Address,
                    Role = u.Role
                })
                .ToListAsync();

            return Ok(users);
        }

        // 2) GET ONE USER
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetUser(int id)
        {
            var u = await _context.Users.FirstOrDefaultAsync(x => x.Id == id);
            if (u == null) return NotFound(new { message = "Không tìm thấy người dùng." });

            return Ok(new UserDto
            {
                Id = u.Id,
                Username = u.Username,
                FullName = u.FullName,
                EmployeeCode = u.EmployeeCode,
                Address = u.Address,
                Role = u.Role
            });
        }

        // 3) CREATE USER
        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserDto request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Username))
                return BadRequest(new { message = "Request không hợp lệ." });

            var username = request.Username.Trim();

            if (await _context.Users.AnyAsync(u => u.Username == username))
                return BadRequest(new { message = "Tên đăng nhập đã tồn tại." });

            if (!string.IsNullOrWhiteSpace(request.EmployeeCode))
            {
                var ec = request.EmployeeCode.Trim();
                if (await _context.Users.AnyAsync(u => u.EmployeeCode == ec))
                    return BadRequest(new { message = "EmployeeCode đã tồn tại." });
            }

            var newUser = new User
            {
                Username = username,
                PasswordHash = request.Password ?? "",
                FullName = request.FullName?.Trim(),
                EmployeeCode = request.EmployeeCode?.Trim(),
                Address = request.Address?.Trim(),
                Role = string.IsNullOrWhiteSpace(request.Role) ? "user" : request.Role!.Trim()
            };

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "✅ Tạo tài khoản thành công",
                id = newUser.Id,
                employeeQrPayload = BuildEmployeePayload(newUser)
            });
        }

        // 4) UPDATE USER
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserDto request)
        {
            if (request == null)
                return BadRequest(new { message = "Request không hợp lệ." });

            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound(new { message = "Không tìm thấy người dùng." });

            if (!string.IsNullOrWhiteSpace(request.EmployeeCode))
            {
                var ec = request.EmployeeCode.Trim();
                var exists = await _context.Users.AnyAsync(u => u.EmployeeCode == ec && u.Id != id);
                if (exists) return BadRequest(new { message = "EmployeeCode đã tồn tại." });

                user.EmployeeCode = ec;
            }
            else
            {
                user.EmployeeCode = null;
            }

            user.FullName = request.FullName?.Trim();
            user.Address = request.Address?.Trim();
            user.Role = request.Role?.Trim();

            if (!string.IsNullOrEmpty(request.Password))
            {
                user.PasswordHash = request.Password;
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "✅ Cập nhật thành công",
                employeeQrPayload = BuildEmployeePayload(user)
            });
        }

        // 5) DELETE USER
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound(new { message = "Không tìm thấy người dùng." });

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            return Ok(new { message = "🗑️ Đã xóa người dùng" });
        }

        // 6) GET EMPLOYEE QR PAYLOAD BY USER ID
        [HttpGet("{id:int}/employee-qr-payload")]
        public async Task<IActionResult> GetEmployeeQrPayload(int id)
        {
            var user = await _context.Users.FirstOrDefaultAsync(x => x.Id == id);
            if (user == null) return NotFound(new { message = "Không tìm thấy người dùng." });

            var payload = BuildEmployeePayload(user);
            if (payload == null)
                return BadRequest(new { message = "User chưa có EmployeeCode để tạo QR." });

            return Ok(new
            {
                user.Id,
                user.Username,
                user.FullName,
                user.EmployeeCode,
                payload
            });
        }

        // 7) GET EMPLOYEE QR PAYLOAD BY CODE/USERNAME
        [HttpPost("employee-qr-payload")]
        public async Task<IActionResult> GetEmployeeQrPayloadByKey([FromBody] EmployeeQrLookupDto req)
        {
            if (req == null)
                return BadRequest(new { message = "Request không hợp lệ." });

            var employeeCode = req.EmployeeCode?.Trim();
            var username = req.Username?.Trim();

            if (string.IsNullOrWhiteSpace(employeeCode) && string.IsNullOrWhiteSpace(username))
                return BadRequest(new { message = "Cần employeeCode hoặc username." });

            User? user = null;

            if (!string.IsNullOrWhiteSpace(employeeCode))
            {
                user = await _context.Users.FirstOrDefaultAsync(u => u.EmployeeCode == employeeCode);
            }

            if (user == null && !string.IsNullOrWhiteSpace(username))
            {
                user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            }

            if (user == null)
                return NotFound(new { message = "Không tìm thấy người dùng." });

            var payload = BuildEmployeePayload(user);
            if (payload == null)
                return BadRequest(new { message = "User chưa có EmployeeCode để tạo QR." });

            return Ok(new
            {
                user.Id,
                user.Username,
                user.FullName,
                user.EmployeeCode,
                payload
            });
        }

        // 8) GET EMPLOYEE QR IMAGE (PNG)
        [HttpGet("{id:int}/employee-qr-image")]
        public async Task<IActionResult> GetEmployeeQrImage(int id)
        {
            var user = await _context.Users.FirstOrDefaultAsync(x => x.Id == id);
            if (user == null) return NotFound(new { message = "Không tìm thấy người dùng." });

            var payload = BuildEmployeePayload(user);
            if (string.IsNullOrWhiteSpace(payload))
                return BadRequest(new { message = "User chưa có EmployeeCode để tạo QR." });

            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(data);
            var bytes = qrCode.GetGraphic(20);

            return File(bytes, "image/png");
        }

        // ======= PRIVATE =======
        private static string? BuildEmployeePayload(User user)
        {
            if (user == null) return null;
            if (string.IsNullOrWhiteSpace(user.EmployeeCode)) return null;

            // ✅ Payload mới: chỉ mã NV, ví dụ: "CTV0013"
            return user.EmployeeCode.Trim();
        }
    }

    // DTOs
    public class UserDto
    {
        public int Id { get; set; }
        public string Username { get; set; } = "";
        public string? FullName { get; set; }
        public string? EmployeeCode { get; set; }
        public string? Address { get; set; }
        public string? Role { get; set; }
    }

    public class CreateUserDto
    {
        public string Username { get; set; } = "";
        public string? Password { get; set; }
        public string? FullName { get; set; }
        public string? EmployeeCode { get; set; }
        public string? Address { get; set; }
        public string? Role { get; set; }
    }

    public class UpdateUserDto
    {
        public string? FullName { get; set; }
        public string? EmployeeCode { get; set; }
        public string? Address { get; set; }
        public string? Role { get; set; }
        public string? Password { get; set; }
    }

    public class EmployeeQrLookupDto
    {
        public string? EmployeeCode { get; set; }
        public string? Username { get; set; }
    }
}
