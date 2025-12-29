using Microsoft.AspNetCore.SignalR;
using WebGhiHinh.Hubs;
using WebGhiHinh.Models; // Chứa User entity
// using WebGhiHinh.DTOs; // Nếu cần dùng DTO

namespace WebGhiHinh.Services
{
    public class QrDispatchService
    {
        private readonly ILogger<QrDispatchService> _logger;
        private readonly FfmpegService _ffmpeg;
        private readonly StationService _stationService;
        private readonly IHubContext<ScanHub> _hubContext;

        // 🔥 BIẾN LƯU THỜI GIAN ĐĂNG NHẬP (Để chống thoát nhầm)
        // Dictionary<StationId, Thời_Gian_Vào>
        private static readonly Dictionary<int, DateTime> _lastLoginTime = new();

        // Thời gian tối thiểu phải làm việc mới được phép quét thoát (Ví dụ: 1 phút)
        private const int MIN_WORKING_MINUTES = 5;

        public QrDispatchService(
            ILogger<QrDispatchService> logger,
            FfmpegService ffmpeg,
            StationService stationService,
            IHubContext<ScanHub> hubContext)
        {
            _logger = logger;
            _ffmpeg = ffmpeg;
            _stationService = stationService;
            _hubContext = hubContext;
        }

        public async Task ProcessScanAsync(string stationName, string qrContent)
        {
            try
            {
                // 1. Lấy thông tin trạm hiện tại từ DB
                var station = await _stationService.GetStationByNameAsync(stationName);
                if (station == null)
                {
                    _logger.LogWarning($"[{stationName}] Không tìm thấy Station trong DB");
                    return;
                }

                _logger.LogInformation($"[{stationName}] Web nhận: {qrContent}");

                // ==================================================
                // 🔥 BƯỚC 1: KIỂM TRA MÃ NHÂN VIÊN (LOGIN / LOGOUT)
                // ==================================================

                // Hàm này trả về Entity User (gồm cả Id, FullName, Username...)
                var userEntity = await _stationService.FindUserEntityByQrAsync(qrContent);

                if (userEntity != null)
                {
                    // --- TRƯỜNG HỢP A: ĐANG TRỰC -> MUỐN THOÁT (LOGOUT) ---
                    // (Người quét chính là người đang ngồi máy)
                    if (station.CurrentUserId == userEntity.Id)
                    {
                        // Kiểm tra thời gian ân hạn (Grace Period)
                        if (_lastLoginTime.TryGetValue(station.Id, out var loginTime))
                        {
                            var duration = DateTime.Now - loginTime;

                            // Nếu chưa đủ thời gian làm việc tối thiểu
                            if (duration.TotalMinutes < MIN_WORKING_MINUTES)
                            {
                                int waitSeconds = 20 - (int)duration.TotalSeconds;
                                _logger.LogWarning($"[{stationName}] ⚠️ Quét quá nhanh! Chờ {waitSeconds}s để thoát.");

                                // Báo cảnh báo màu vàng xuống UI
                                await _hubContext.Clients.All.SendAsync("ScanResult",
                                    stationName,
                                    $"⚠️ Chờ {waitSeconds}s nữa để thoát!",
                                    0, 0, 0, 0);

                                return; // ⛔ NGĂN KHÔNG CHO THOÁT
                            }
                        }

                        // Nếu đã đủ thời gian -> Thực hiện Đăng xuất
                        _logger.LogInformation($"[{stationName}] 🚪 Đăng xuất: {userEntity.FullName}");

                        await _stationService.LogoutStationAsync(station.Id);

                        // Xóa khỏi bộ nhớ đệm
                        _lastLoginTime.Remove(station.Id);

                        // Báo UI: Đã thoát (Màu đỏ)
                        await _hubContext.Clients.All.SendAsync("ScanResult",
                            stationName,
                            "🔴 ĐÃ ĐĂNG XUẤT",
                            0, 0, 0, 0);
                    }

                    // --- TRƯỜNG HỢP B: NGƯỜI MỚI -> ĐĂNG NHẬP (LOGIN) ---
                    // (Máy trống hoặc người khác đang trực -> Đá người cũ ra, người mới vào)
                    else
                    {
                        _logger.LogInformation($"[{stationName}] 🔑 Đăng nhập: {userEntity.FullName}");

                        // Cập nhật người trực máy vào DB
                        await _stationService.UpdateCurrentUserAsync(station.Id, userEntity.Username);

                        // Lưu thời gian bắt đầu làm việc
                        _lastLoginTime[station.Id] = DateTime.Now;

                        // Báo UI: Xin chào
                        await _hubContext.Clients.All.SendAsync("ScanResult",
                            stationName,
                            $"Xin chào {userEntity.FullName}!",
                            0, 0, 0, 0);
                    }
                    return; // Kết thúc xử lý nhân viên
                }

                // ==================================================
                // 🔥 BƯỚC 2: XỬ LÝ SẢN PHẨM (GHI HÌNH)
                // ==================================================
                // (Chỉ chạy xuống đây nếu mã QR KHÔNG PHẢI là nhân viên)

                // Kiểm tra: Máy này đã có ai đăng nhập chưa?
                if (string.IsNullOrEmpty(station.CurrentUsername) || station.CurrentUsername == "---")
                {
                    _logger.LogWarning($"[{stationName}] ⚠️ Chưa đăng nhập, từ chối ghi hình: {qrContent}");

                    await _hubContext.Clients.All.SendAsync("ScanResult",
                        stationName,
                        "⚠️ Vui lòng quét thẻ Nhân viên trước!",
                        0, 0, 0, 0);
                    return;
                }

                _logger.LogInformation($"[{stationName}] 🎬 Bắt đầu ghi hình đơn: {qrContent}");

                // Lấy link RTSP
                string rtspMain = station.OverviewCamera?.RtspUrl ?? "";
                string rtspQr = station.QrCamera?.RtspUrl ?? "";

                if (!string.IsNullOrEmpty(rtspMain))
                {
                    // Gọi FFmpeg Service
                    _ffmpeg.StartRecording(
                        stationName,
                        station.CurrentUsername,
                        qrContent, // Mã sản phẩm làm tên file
                        rtspMain,
                        rtspQr
                    );

                    // Báo trạng thái đang quay lên UI (Chữ màu đỏ)
                    await _hubContext.Clients.All.SendAsync("ScanResult",
                        stationName,
                        $"🔴 REC: {qrContent}",
                        0, 0, 0, 0);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[{stationName}] Lỗi xử lý QR tại Dispatcher");
            }
        }
    }
}