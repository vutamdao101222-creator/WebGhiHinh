using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using WebGhiHinh.Data;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace WebGhiHinh.Services
{
    public class VideoCleanupService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IWebHostEnvironment _env;

        public VideoCleanupService(IServiceProvider serviceProvider, IWebHostEnvironment env)
        {
            _serviceProvider = serviceProvider;
            _env = env;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await DoCleanup();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Cleanup Error] {ex.Message}");
                }

                // Chờ 24 giờ rồi chạy lại
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }

        private async Task DoCleanup()
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // 1. Đọc cài đặt số ngày
                int retentionDays = 30;
                string settingPath = Path.Combine(_env.ContentRootPath, "retention_settings.txt");
                if (File.Exists(settingPath))
                {
                    if (int.TryParse(File.ReadAllText(settingPath), out int days))
                    {
                        retentionDays = days;
                    }
                }

                Console.WriteLine($"[Cleanup] Đang quét video cũ hơn {retentionDays} ngày...");

                // 2. Tìm các video hết hạn
                var thresholdDate = DateTime.Now.AddDays(-retentionDays);
                var oldVideos = await context.VideoLogs
                    .Where(v => v.StartTime < thresholdDate)
                    .ToListAsync();

                if (oldVideos.Count == 0) return;

                // 3. Xóa File và DB
                foreach (var video in oldVideos)
                {
                    // Xóa file vật lý
                    try
                    {
                        string baseVideoPath = @"C:\GhiHinhVideos";
                        string fileName = Path.GetFileName(video.FilePath);
                        string fullPath = Path.Combine(baseVideoPath, video.StationName, fileName);

                        if (File.Exists(fullPath))
                        {
                            File.Delete(fullPath);
                            Console.WriteLine($"[Deleted File] {fullPath}");
                        }
                    }
                    catch { /* Kệ lỗi file */ }

                    // Xóa DB
                    context.VideoLogs.Remove(video);
                }

                await context.SaveChangesAsync();
                Console.WriteLine($"[Cleanup] Đã xóa {oldVideos.Count} video cũ.");
            }
        }
    }
}