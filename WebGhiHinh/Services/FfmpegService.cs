using System.Diagnostics;

namespace WebGhiHinh.Services
{
    public class FfmpegService
    {
        // Lưu trữ các tiến trình đang chạy: Key = Mã QR (hoặc IP Camera), Value = Process
        private static Dictionary<string, Process> _activeProcesses = new Dictionary<string, Process>();

        // Thư mục lưu video
        private readonly string _outputFolder = @"C:\GhiHinhVideos"; // Bạn có thể đổi đường dẫn này

        public FfmpegService()
        {
            if (!Directory.Exists(_outputFolder))
            {
                Directory.CreateDirectory(_outputFolder);
            }
        }

        // Bắt đầu ghi hình
        public string StartRecording(string rtspUrl, string qrCode, string cameraName)
        {
            // 1. Kiểm tra nếu đang ghi QR này rồi thì thôi
            if (_activeProcesses.ContainsKey(qrCode))
            {
                return "Đang ghi hình cho mã này rồi!";
            }

            // 2. Tạo tên file: VIDEO_MaQR_NgayGio.mp4
            string fileName = $"VIDEO_{qrCode}_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
            string filePath = Path.Combine(_outputFolder, fileName);

            // 3. Cấu hình lệnh FFmpeg
            // Lưu ý: Đảm bảo bạn đã copy file ffmpeg.exe vào thư mục gốc của dự án
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg.exe",
                // Lệnh: -rtsp_transport tcp (để ổn định) -i (input) -c copy (không encode lại cho nhẹ CPU)
                Arguments = $"-rtsp_transport tcp -i \"{rtspUrl}\" -c copy -y \"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true // Chạy ngầm, không hiện cửa sổ đen
            };

            // 4. Khởi chạy tiến trình
            try
            {
                Process process = new Process { StartInfo = processStartInfo };
                process.Start();

                // Lưu tiến trình vào bộ nhớ để sau này còn tắt
                _activeProcesses.Add(qrCode, process);

                Console.WriteLine($"[INFO] Bắt đầu ghi hình: {qrCode}");
                return fileName; // Trả về tên file để lưu vào Database
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Không thể bật FFmpeg: {ex.Message}");
                throw;
            }
        }

        // Dừng ghi hình
        public bool StopRecording(string qrCode)
        {
            if (_activeProcesses.ContainsKey(qrCode))
            {
                try
                {
                    var process = _activeProcesses[qrCode];
                    if (!process.HasExited)
                    {
                        // Gửi lệnh 'q' để dừng mềm hoặc Kill để dừng cứng
                        process.Kill();
                        process.WaitForExit();
                    }
                    _activeProcesses.Remove(qrCode);
                    Console.WriteLine($"[INFO] Đã dừng ghi hình: {qrCode}");
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Lỗi khi dừng ghi: {ex.Message}");
                    return false;
                }
            }
            return false; // Không tìm thấy tiến trình nào đang chạy với mã này
        }
    }
}