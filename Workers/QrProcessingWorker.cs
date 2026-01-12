using Microsoft.AspNetCore.SignalR;
using WebGhiHinh.Hubs;
using WebGhiHinh.Models;

namespace WebGhiHinh.Workers
{
    public class QrProcessingWorker : BackgroundService
    {
        private readonly IQrScanQueue _queue;
        private readonly ILogger<QrProcessingWorker> _logger;
        private readonly IServiceProvider _serviceProvider;

        public QrProcessingWorker(IQrScanQueue queue, ILogger<QrProcessingWorker> logger, IServiceProvider serviceProvider)
        {
            _queue = queue;
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🚀 QR Processing Worker đã khởi động...");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // 1. Lấy item từ hàng đợi (Sẽ chờ ở đây nếu hàng đợi rỗng)
                    var request = await _queue.DequeueAsync(stoppingToken);

                    _logger.LogInformation($"⚡ Đang xử lý QR: {request.QrCode} từ trạm {request.StationName}");

                    // 2. Xử lý Logic (Bắn SignalR, Ghi Database...)
                    await ProcessRequestAsync(request);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi xử lý hàng đợi QR");
                }
            }
        }

        private async Task ProcessRequestAsync(QrScanRequest request)
        {
            using var scope = _serviceProvider.CreateScope();
            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ScanHub>>();

            // Ví dụ: Bắn SignalR
            await hub.Clients.All.SendAsync("ScanHit", new
            {
                StationName = request.StationName,
                Code = request.QrCode
            });

            // Ví dụ: Lưu DB hoặc Gọi FFmpegService ở đây
        }
    }
}