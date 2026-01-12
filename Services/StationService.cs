using Microsoft.EntityFrameworkCore;
using WebGhiHinh.Data;
using WebGhiHinh.DTOs;
using WebGhiHinh.Models;
using System.Net.Http.Json;
// 👇 Quan trọng: Để gọi hàm CheckIsRecording từ QrDispatchService
using WebGhiHinh.Services;

namespace WebGhiHinh.Services
{
    public class StationService
    {
        private readonly IDbContextFactory<AppDbContext> _factory;
        private readonly HttpClient _http; // Dùng để gọi API “Kill worker” nếu cần

        public StationService(IDbContextFactory<AppDbContext> factory, HttpClient http)
        {
            _factory = factory;
            _http = http;
        }

        // ==========================================
        // PHẦN 1: GET DỮ LIỆU (CỐT LÕI)
        // ==========================================

        // Lấy danh sách tất cả trạm để hiển thị lên trang Live
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
                    CurrentUsername = s.CurrentUser?.Username ?? "---",

                    OverviewCameraId = s.OverviewCameraId,
                    OverviewCamera = s.OverviewCamera != null ? new CameraMiniDto
                    {
                        Id = s.OverviewCamera.Id,
                        Name = s.OverviewCamera.Name,
                        RtspUrl = s.OverviewCamera.RtspUrl
                    } : null,

                    QrCameraId = s.QrCameraId,
                    QrCamera = s.QrCamera != null ? new CameraMiniDto
                    {
                        Id = s.QrCamera.Id,
                        Name = s.QrCamera.Name,
                        RtspUrl = s.QrCamera.RtspUrl
                    } : null,

                    // 🔥 QUAN TRỌNG: Lấy trạng thái thực tế từ RAM của hệ thống
                    // Giúp khi F5 trang web vẫn biết máy nào đang quay
                    IsRecording = QrDispatchService.CheckIsRecording(s.Id)

                }).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"🔥 Lỗi GetStationsAsync: {ex.Message}");
                return new List<StationDto>();
            }
        }

        // Lấy thông tin 1 trạm cụ thể (Dùng cho Worker)
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
                CurrentUsername = s.CurrentUser?.Username ?? "---",

                OverviewCamera = s.OverviewCamera != null ? new CameraMiniDto
                {
                    Id = s.OverviewCamera.Id,
                    Name = s.OverviewCamera.Name,
                    RtspUrl = s.OverviewCamera.RtspUrl
                } : null,

                QrCamera = s.QrCamera != null ? new CameraMiniDto
                {
                    Id = s.QrCamera.Id,
                    Name = s.QrCamera.Name,
                    RtspUrl = s.QrCamera.RtspUrl
                } : null,

                // 🔥 Cũng đồng bộ trạng thái quay ở đây cho chắc chắn
                IsRecording = QrDispatchService.CheckIsRecording(s.Id)
            };
        }

        // ==========================================
        // PHẦN 2: LOGIC QUÉT QR & USER
        // ==========================================

        public async Task<User?> FindUserEntityByQrAsync(string qrContent)
        {
            using var context = await _factory.CreateDbContextAsync();
            string clean = qrContent.Trim();
            // Tìm user theo Mã NV hoặc Username
            return await context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.EmployeeCode == clean || u.Username == clean);
        }

        public async Task LogoutStationAsync(int stationId)
        {
            using var context = await _factory.CreateDbContextAsync();
            var station = await context.Stations.FindAsync(stationId);
            if (station != null)
            {
                station.CurrentUserId = null;
                await context.SaveChangesAsync();
            }
        }

        public async Task UpdateCurrentUserAsync(int stationId, string username)
        {
            using var context = await _factory.CreateDbContextAsync();
            var user = await context.Users.FirstOrDefaultAsync(u => u.Username == username || u.EmployeeCode == username);
            if (user == null) return;

            var station = await context.Stations.FindAsync(stationId);
            if (station != null)
            {
                station.CurrentUserId = user.Id;
                await context.SaveChangesAsync();
            }
        }

        // ==========================================
        // PHẦN 3: ADMIN CRUD (QUẢN LÝ TRẠM)
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

        // ==========================================
        // PHẦN 4: CÁC TIỆN ÍCH KHÁC (KILL WORKER / FORCE RELEASE)
        // ==========================================

        public async Task<bool> KillWorkerAsync(Guid stationGuid)
        {
            try
            {
                var response = await _http.PostAsJsonAsync($"api/stations/{stationGuid}/kill", new { });
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> ForceReleaseAsync(int id)
        {
            try
            {
                await LogoutStationAsync(id);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}