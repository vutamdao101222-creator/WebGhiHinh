using Microsoft.EntityFrameworkCore;
using WebGhiHinh.Data;
using WebGhiHinh.DTOs;
using WebGhiHinh.Models;

namespace WebGhiHinh.Services
{
    public class StationService
    {
        private readonly IDbContextFactory<AppDbContext> _factory;

        public StationService(IDbContextFactory<AppDbContext> factory)
        {
            _factory = factory;
        }

        // ==========================================
        // PHẦN 1: CÁC HÀM GET DỮ LIỆU
        // ==========================================
        public async Task<List<StationDto>> GetStationsAsync()
        {
            using var context = await _factory.CreateDbContextAsync();
            try
            {
                var entities = await context.Stations
                    .Include(s => s.OverviewCamera)
                    .Include(s => s.QrCamera)
                    .Include(s => s.CurrentUser)
                    .AsNoTracking()
                    .ToListAsync();

                return entities.Select(s => new StationDto
                {
                    Id = s.Id,
                    Name = s.Name ?? "Không tên",
                    CurrentUserId = s.CurrentUserId,
                    CurrentUsername = s.CurrentUser?.Username ?? "---", // Lấy username hoặc tên hiển thị
                    // Map Camera...
                    OverviewCameraId = s.OverviewCameraId,
                    OverviewCamera = (s.OverviewCamera != null) ? new CameraMiniDto
                    {
                        Id = s.OverviewCamera.Id,
                        Name = s.OverviewCamera.Name,
                        RtspUrl = s.OverviewCamera.RtspUrl
                    } : null,
                    QrCameraId = s.QrCameraId,
                    QrCamera = (s.QrCamera != null) ? new CameraMiniDto
                    {
                        Id = s.QrCamera.Id,
                        Name = s.QrCamera.Name,
                        RtspUrl = s.QrCamera.RtspUrl
                    } : null
                }).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"🔥 LỖI STATION SERVICE: {ex.Message}");
                return new List<StationDto>();
            }
        }

        public async Task<StationDto?> GetStationByNameAsync(string name)
        {
            using var context = await _factory.CreateDbContextAsync();
            var s = await context.Stations
                .Include(x => x.OverviewCamera)
                .Include(x => x.QrCamera)
                .Include(x => x.CurrentUser)
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Name == name);

            if (s == null) return null;

            return new StationDto
            {
                Id = s.Id,
                Name = s.Name,
                CurrentUserId = s.CurrentUserId,
                CurrentUsername = s.CurrentUser?.Username ?? "---", // Lấy tên user
                OverviewCamera = s.OverviewCamera != null ? new CameraMiniDto { Id = s.OverviewCamera.Id, Name = s.OverviewCamera.Name, RtspUrl = s.OverviewCamera.RtspUrl } : null,
                QrCamera = s.QrCamera != null ? new CameraMiniDto { Id = s.QrCamera.Id, Name = s.QrCamera.Name, RtspUrl = s.QrCamera.RtspUrl } : null
            };
        }

        // ==========================================
        // PHẦN 2: CÁC HÀM XỬ LÝ LOGIC QUÉT QR (QUAN TRỌNG)
        // ==========================================

        // 👇 1. Hàm tìm User trả về Full Entity (ĐỂ SỬA LỖI FindUserEntityByQrAsync)
        public async Task<User?> FindUserEntityByQrAsync(string qrContent)
        {
            using var context = await _factory.CreateDbContextAsync();
            string clean = qrContent.Trim();

            // Tìm theo Mã nhân viên (EmployeeCode) HOẶC Username
            return await context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.EmployeeCode == clean || u.Username == clean);
        }

        // 👇 2. Hàm Đăng xuất (ĐỂ SỬA LỖI LogoutStationAsync)
        public async Task LogoutStationAsync(int stationId)
        {
            using var context = await _factory.CreateDbContextAsync();
            var station = await context.Stations.FindAsync(stationId);

            if (station != null)
            {
                station.CurrentUserId = null; // Xóa người trực
                await context.SaveChangesAsync();
            }
        }

        // 3. Hàm Đăng nhập (UpdateCurrentUserAsync)
        public async Task UpdateCurrentUserAsync(int stationId, string username)
        {
            using var context = await _factory.CreateDbContextAsync();

            // Tìm User
            var user = await context.Users.FirstOrDefaultAsync(u => u.Username == username || u.EmployeeCode == username);
            if (user == null) return;

            // Update Station
            var station = await context.Stations.FindAsync(stationId);
            if (station != null)
            {
                station.CurrentUserId = user.Id;
                await context.SaveChangesAsync();
            }
        }

        // ==========================================
        // PHẦN 3: CÁC HÀM ADMIN CRUD (GIỮ NGUYÊN)
        // ==========================================
        public async Task<List<Station>> GetAllStationsForAdminAsync()
        {
            using var context = await _factory.CreateDbContextAsync();
            return await context.Stations
                .Include(s => s.OverviewCamera)
                .Include(s => s.QrCamera)
                .Include(s => s.CurrentUser)
                .OrderBy(s => s.Name)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<List<Camera>> GetAllCamerasAsync()
        {
            using var context = await _factory.CreateDbContextAsync();
            return await context.Cameras.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
        }

        public async Task<bool> CreateStationAsync(string name)
        {
            using var context = await _factory.CreateDbContextAsync();
            if (await context.Stations.AnyAsync(s => s.Name == name)) return false;
            context.Stations.Add(new Station { Name = name });
            return await context.SaveChangesAsync() > 0;
        }

        public async Task<bool> UpdateCamerasAsync(int stationId, int? overviewId, int? qrId)
        {
            using var context = await _factory.CreateDbContextAsync();
            var station = await context.Stations.FindAsync(stationId);
            if (station == null) return false;
            station.OverviewCameraId = overviewId;
            station.QrCameraId = qrId;
            return await context.SaveChangesAsync() > 0;
        }

        public async Task<bool> DeleteStationAsync(int id)
        {
            using var context = await _factory.CreateDbContextAsync();
            var station = await context.Stations.FindAsync(id);
            if (station == null) return false;
            context.Stations.Remove(station);
            return await context.SaveChangesAsync() > 0;
        }

        // Hàm cũ trả về string (có thể giữ lại hoặc bỏ nếu không dùng nữa)
        public async Task<string?> FindUserByQrAsync(string qrContent)
        {
            var user = await FindUserEntityByQrAsync(qrContent);
            return user?.EmployeeCode;
        }

        public async Task<bool> ForceReleaseAsync(int id)
        {
            return await LogoutStationAsync(id).ContinueWith(t => true);
        }
    }
}