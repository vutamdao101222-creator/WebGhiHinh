using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebGhiHinh.Data;
using WebGhiHinh.Models;

namespace WebGhiHinh.Controllers
{
    [Route("api/users")]
    [ApiController]
    [Authorize(Roles = "admin")] // Chỉ Admin mới được truy cập
    public class UsersController : ControllerBase
    {
        private readonly AppDbContext _context;

        public UsersController(AppDbContext context)
        {
            _context = context;
        }

        // 1. Lấy danh sách User (GET: api/users)
        [HttpGet]
        public async Task<IActionResult> GetUsers()
        {
            var users = await _context.Users
                .Select(u => new UserDto
                {
                    Id = u.Id,
                    Username = u.Username,
                    FullName = u.FullName,
                    EmployeeCode = u.EmployeeCode,
                    Address = u.Address,
                    Role = u.Role,
                    // Không trả về PasswordHash để bảo mật
                })
                .ToListAsync();

            return Ok(users);
        }

        // 2. Tạo User mới (POST: api/users)
        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserDto request)
        {
            if (await _context.Users.AnyAsync(u => u.Username == request.Username))
            {
                return BadRequest("Tên đăng nhập đã tồn tại.");
            }

            var newUser = new User
            {
                Username = request.Username,
                PasswordHash = request.Password, // Lưu ý: Nên mã hóa trong thực tế
                FullName = request.FullName,
                EmployeeCode = request.EmployeeCode,
                Address = request.Address,
                Role = request.Role ?? "user"
            };

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Tạo tài khoản thành công" });
        }

        // 3. Cập nhật User (PUT: api/users/{id})
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserDto request)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound("Không tìm thấy người dùng.");

            user.FullName = request.FullName;
            user.EmployeeCode = request.EmployeeCode;
            user.Address = request.Address;
            user.Role = request.Role;

            // Nếu có nhập mật khẩu mới thì cập nhật, không thì giữ nguyên
            if (!string.IsNullOrEmpty(request.Password))
            {
                user.PasswordHash = request.Password;
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Cập nhật thành công" });
        }

        // 4. Xóa User (DELETE: api/users/{id})
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            // Chặn không cho xóa chính mình (nếu cần)
            // var currentUserName = User.Identity.Name;
            // if (user.Username == currentUserName) return BadRequest("Không thể xóa chính mình.");

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Đã xóa người dùng" });
        }
    }

    // --- DTO Classes ---
    public class UserDto
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string FullName { get; set; }
        public string EmployeeCode { get; set; }
        public string Address { get; set; }
        public string Role { get; set; }
    }

    public class CreateUserDto
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string FullName { get; set; }
        public string EmployeeCode { get; set; }
        public string Address { get; set; }
        public string Role { get; set; }
    }

    public class UpdateUserDto
    {
        public string FullName { get; set; }
        public string EmployeeCode { get; set; }
        public string Address { get; set; }
        public string Role { get; set; }
        public string? Password { get; set; } // Cho phép null nếu không đổi pass
    }
}