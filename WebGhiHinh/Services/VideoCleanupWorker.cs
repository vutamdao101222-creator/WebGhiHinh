using WebGhiHinh.Data;
using Microsoft.EntityFrameworkCore;

namespace WebGhiHinh.Services
{
    public class VideoCleanupWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly SystemSettingsService _settings;
        private readonly ILogger<VideoCleanupWorker> _logger;
        private readonly string _videoPath = @"C:\GhiHinhVideos"; // Đảm bảo khớp Program.cs

        public VideoCleanupWorker(IServiceProvider serviceProvider, SystemSettingsService settings, ILogger<VideoCleanupWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _settings = settings;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("⏳ Đang quét dọn video cũ...");
                    await CleanupVideos();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi dọn dẹp video");
                }

                // Chờ 24 giờ mới chạy lại (hoặc 1 giờ tùy bạn)
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }

        private async Task CleanupVideos()
        {
            int days = _settings.RetentionDays;
            if (days <= 0) return; // Nếu = 0 thì không xóa

            var cutoffTime = DateTime.Now.AddDays(-days);
            _logger.LogInformation($"🗑️ Xóa video cũ hơn: {cutoffTime} ({days} ngày)");

            using (var scope = _serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // 1. Tìm video hết hạn trong DB
                var oldVideos = await dbContext.VideoLogs
                    .Where(v => v.StartTime < cutoffTime)
                    .ToListAsync();

                if (oldVideos.Any())
                {
                    foreach (var video in oldVideos)
                    {
                        // Xóa file vật lý
                        if (File.Exists(video.FilePath))
                        {
                            try
                            {
                                File.Delete(video.FilePath);
                                _logger.LogInformation($"Đã xóa file: {video.FilePath}");
                            }
                            catch { /* Bỏ qua lỗi file đang dùng */ }
                        }
                    }

                    // Xóa record trong DB
                    dbContext.VideoLogs.RemoveRange(oldVideos);
                    await dbContext.SaveChangesAsync();
                    _logger.LogInformation($"✅ Đã xóa {oldVideos.Count} video hết hạn.");
                }
            }
        }
    }
}