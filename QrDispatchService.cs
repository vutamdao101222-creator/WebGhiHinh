using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using WebGhiHinh.Data;
using WebGhiHinh.DTOs;
using WebGhiHinh.Hubs;
using WebGhiHinh.Models;

namespace WebGhiHinh.Services
{
    public class QrDispatchService
    {
        private readonly ILogger<QrDispatchService> _logger;
        private readonly FfmpegService _ffmpeg;
        private readonly StationService _stationService;
        private readonly IHubContext<ScanHub> _hubContext;
        private readonly IServiceProvider _serviceProvider;

        // Lưu thời gian login để tính toán thời gian làm việc tối thiểu
        private static readonly ConcurrentDictionary<int, DateTime> _lastLoginTime = new();

        // 🔥 QUAN TRỌNG: Lưu mã sản phẩm đang quay của từng trạm (RAM)
        // Key: StationId, Value: Mã QR đang quay
        private static readonly ConcurrentDictionary<int, string> _currentRecordingQr = new();

        private const int MIN_WORKING_MINUTES = 2;

        public QrDispatchService(
            ILogger<QrDispatchService> logger,
            FfmpegService ffmpeg,
            StationService stationService,
            IHubContext<ScanHub> hubContext,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _ffmpeg = ffmpeg;
            _stationService = stationService;
            _hubContext = hubContext;
            _serviceProvider = serviceProvider;
        }

        // =========================================================
        // HÀM TIỆN ÍCH CHO STATION SERVICE GỌI (Để lấy trạng thái khi load trang)
        // =========================================================
        public static bool CheckIsRecording(int stationId)
        {
            return _currentRecordingQr.ContainsKey(stationId);
        }

        // ===================== MAIN PROCESS =====================
        public async Task ProcessScanAsync(string stationName, string qrContent)
        {
            try
            {
                // 1️⃣ Lấy thông tin trạm
                var station = await _stationService.GetStationByNameAsync(stationName);
                if (station == null)
                {
                    _logger.LogWarning("Station {Station} không tồn tại", stationName);
                    return;
                }

                _logger.LogInformation("[{Station}] Nhận QR: {Qr}", stationName, qrContent);

                // 2️⃣ Kiểm tra User (Đăng nhập/Xuất)
                var userEntity = await _stationService.FindUserEntityByQrAsync(qrContent);
                if (userEntity != null)
                {
                    await HandleUserQrAsync(station, userEntity);
                    return;
                }

                // 🔥 3️⃣ LOGIC XỬ LÝ LỆNH "STOP RECORDING"
                if (qrContent == "STOP RECORDING")
                {
                    _logger.LogInformation("[{Station}] 🛑 Nhận lệnh DỪNG QUAY.", stationName);

                    // Gọi lệnh dừng bên FFmpeg
                    _ffmpeg.StopRecording(stationName);

                    // Xóa trạng thái đang quay khỏi RAM
                    _currentRecordingQr.TryRemove(station.Id, out _);

                    // Báo cho giao diện (SignalR) biết là ĐÃ DỪNG (false)
                    await SafeSendAsync(stationName, "🛑 ĐÃ DỪNG QUAY");
                    await _hubContext.Clients.All.SendAsync("UpdateRecordingStatus", stationName, false);

                    return; // 👈 Return ngay để KHÔNG chạy xuống phần quay mới
                }

                // 4️⃣ Nếu không phải User, không phải STOP -> Là Mã Sản Phẩm -> Quay mới
                await HandleProductQrAsync(station, qrContent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Station}] Lỗi xử lý QR: {Message}", stationName, ex.Message);
            }
        }

        // ===================== HANDLE USER QR (Đăng nhập / Đăng xuất) =====================
        private async Task HandleUserQrAsync(StationDto station, User user)
        {
            string stationName = station.Name ?? "Unknown";

            // A. Nếu người đang trực quét lại mã của mình -> Đăng xuất
            if (station.CurrentUserId == user.Id)
            {
                if (_lastLoginTime.TryGetValue(station.Id, out var loginTime))
                {
                    var duration = DateTime.Now - loginTime;
                    if (duration.TotalMinutes < MIN_WORKING_MINUTES)
                    {
                        int waitSeconds = (int)((MIN_WORKING_MINUTES * 60) - duration.TotalSeconds);
                        await SafeSendAsync(stationName, $"⚠️ Chờ {waitSeconds}s nữa để thoát!");
                        return;
                    }
                }

                _logger.LogInformation("[{Station}] 🚪 Đăng xuất: {User}", stationName, user.FullName);

                // Dừng quay nếu đang quay dở trước khi logout
                if (_currentRecordingQr.TryRemove(station.Id, out var recordingQr))
                {
                    _ffmpeg.StopRecording(stationName);
                    // Cập nhật trạng thái DỪNG lên giao diện
                    await _hubContext.Clients.All.SendAsync("UpdateRecordingStatus", stationName, false);
                }

                await _stationService.LogoutStationAsync(station.Id);
                _lastLoginTime.TryRemove(station.Id, out _);

                await SafeSendAsync(stationName, "🔴 ĐÃ ĐĂNG XUẤT");
                return;
            }

            // B. Người mới đăng nhập
            _logger.LogInformation("[{Station}] 🔑 Đăng nhập: {User}", stationName, user.FullName);

            await _stationService.UpdateCurrentUserAsync(station.Id, user.Username);
            _lastLoginTime[station.Id] = DateTime.Now;

            await SafeSendAsync(stationName, $"Xin chào {user.FullName}!");
        }

        // ===================== HANDLE PRODUCT QR (Ghi hình) =====================
        private async Task HandleProductQrAsync(StationDto station, string qrContent)
        {
            string stationName = station.Name ?? "Unknown";

            // 1. Kiểm tra xem đã đăng nhập chưa
            if (string.IsNullOrEmpty(station.CurrentUsername) || station.CurrentUsername == "---")
            {
                await SafeSendAsync(stationName, "⚠️ Chưa đăng nhập!");
                return;
            }

            // 2. Logic Chặn Trùng Lặp & Dừng cái cũ
            if (_currentRecordingQr.TryGetValue(station.Id, out string currentQr))
            {
                if (currentQr == qrContent)
                {
                    // Đang quay đúng mã này rồi -> Bỏ qua
                    _logger.LogInformation("[{Station}] Đang quay {Qr}, bỏ qua lệnh trùng.", stationName, qrContent);
                    return;
                }

                // Nếu quét mã mới khi mã cũ chưa xong -> Dừng mã cũ trước
                _logger.LogInformation("[{Station}] Đổi mã từ {Old} sang {New}. Dừng quay cũ.", stationName, currentQr, qrContent);
                _ffmpeg.StopRecording(stationName);
                _currentRecordingQr.TryRemove(station.Id, out _);
            }

            // 3. Kiểm tra cấu hình Camera
            string rtspMain = station.OverviewCamera?.RtspUrl ?? "";
            string rtspQr = station.QrCamera?.RtspUrl ?? "";

            if (string.IsNullOrEmpty(rtspMain))
            {
                await SafeSendAsync(stationName, "⚠️ Thiếu Camera chính!");
                return;
            }

            // 4. Bắt đầu quay
            _logger.LogInformation("[{Station}] 🎬 Bắt đầu quay: {Qr}", stationName, qrContent);

            _ffmpeg.StartRecording(
                stationName,
                station.CurrentUsername ?? "unknown",
                qrContent,
                rtspMain,
                rtspQr
            );

            // 5. Lưu trạng thái ĐANG QUAY vào RAM
            _currentRecordingQr[station.Id] = qrContent;

            // 6. Gửi thông báo UI:
            // - Hiển thị dòng chữ "REC: Mã SP"
            // - Bật cờ IsRecording = true (để hiện viền đỏ nhấp nháy)
            await SafeSendAsync(stationName, $"🔴 REC: {qrContent}");
            await _hubContext.Clients.All.SendAsync("UpdateRecordingStatus", stationName, true);
        }

        // ===================== SAFE SIGNALR HELPER =====================
        private async Task SafeSendAsync(string station, string message)
        {
            try
            {
                await _hubContext.Clients.All.SendAsync("ScanResult", station, message, 0, 0, 0, 0);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi gửi SignalR");
            }
        }
    }
}