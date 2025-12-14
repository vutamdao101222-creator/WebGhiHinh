using Microsoft.EntityFrameworkCore;
using WebGhiHinh.Models;
using WebGhiHinh.Models;

namespace WebGhiHinh.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // Khai báo danh sách các bảng sẽ tạo trong GHIHINH1
        public DbSet<User> Users { get; set; }
        public DbSet<Camera> Cameras { get; set; }
        public DbSet<Station> Stations { get; set; }
        public DbSet<VideoLog> VideoLogs { get; set; }
    }
}