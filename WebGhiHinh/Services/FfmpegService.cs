using System.Diagnostics;
using System.Collections.Concurrent;
using System.Text;

namespace WebGhiHinh.Services
{
    public class FfmpegService
    {
        // Key = stationName → mỗi trạm chỉ có 1 tiến trình FFmpeg
        private static readonly ConcurrentDictionary<string, Process> _activeProcesses = new();

        // ⚠️ LƯU Ý: Đảm bảo ứng dụng có quyền Ghi vào thư mục này. 
        private readonly string _outputFolder = @"C:\GhiHinhVideos";

        public FfmpegService()
        {
            if (!Directory.Exists(_outputFolder))
                Directory.CreateDirectory(_outputFolder);
        }

        public string StartRecording(string rtspUrl, string qrCode, string stationName, string userName)
        {
            string stationKey = stationName.ToLower();

            // Nếu trạm này đang ghi → dừng lại trước
            if (_activeProcesses.ContainsKey(stationKey))
            {
                StopRecording(stationName);
            }

            // Tạo thư mục trạm
            string stationFolder = Path.Combine(_outputFolder, stationName);
            if (!Directory.Exists(stationFolder))
                Directory.CreateDirectory(stationFolder);

            // Chuẩn hóa tên file
            string safeQr = qrCode.Replace("/", "_").Replace("\\", "_").Replace(":", "");
            string safeUser = userName.Replace("/", "_").Replace("\\", "_").Replace(" ", "");
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            string fileName = $"{safeUser}_{safeQr}_{timestamp}.mp4";
            string fullPath = Path.Combine(stationFolder, fileName);
            string logPath = Path.Combine(stationFolder, "ffmpeg_debug.log"); // File log để debug

            // Tìm file ffmpeg.exe
            string ffmpegPath = FindFfmpegPath();
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                throw new FileNotFoundException($"Không tìm thấy 'ffmpeg.exe'. Hãy copy nó vào: {AppDomain.CurrentDomain.BaseDirectory}");
            }

            // 👇 ARGUMENTS QUAN TRỌNG:
            // -an: Tắt ghi âm (Tránh crash nếu Camera không có mic)
            // -rtsp_transport tcp: Bắt buộc dùng TCP để hình ảnh không bị vỡ (artifact)
            string arguments = $"-y -rtsp_transport tcp -i \"{rtspUrl}\" -c:v libx264 -preset ultrafast -pix_fmt yuv420p -an \"{fullPath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true, // Bắt buộc để bắt lỗi
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                Process proc = new Process { StartInfo = psi };

                // 👇 GHI LOG LỖI RA FILE ĐỂ BẠN DỄ TRA CỨU
                proc.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        // Ghi lỗi vào file log (Append)
                        try
                        {
                            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {e.Data}{Environment.NewLine}");
                        }
                        catch { }

                        // Vẫn in ra Console để xem live
                        Console.WriteLine($"[FFMPEG - {stationName}] {e.Data}");
                    }
                };

                proc.Start();
                proc.BeginErrorReadLine();

                // 👇 TĂNG THỜI GIAN CHỜ LÊN 2 GIÂY (2000ms)
                // RTSP cần thời gian bắt tay (handshake), 500ms là quá nhanh, dễ bị lọt lỗi.
                if (proc.WaitForExit(2000))
                {
                    string errorMsg = $"FFmpeg tắt đột ngột (Code: {proc.ExitCode}). Xem chi tiết tại: {logPath}";
                    Console.WriteLine($"[ERROR] {errorMsg}");
                    throw new Exception(errorMsg);
                }

                _activeProcesses.TryAdd(stationKey, proc);

                Console.WriteLine($"[INFO] Đang ghi hình tại {stationName}: {fileName}");

                // Trả về đường dẫn tương đối để lưu vào DB
                return Path.Combine(stationName, fileName).Replace("\\", "/");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FATAL ERROR] Lỗi khởi động FFmpeg: {ex.Message}");
                throw;
            }
        }

        public bool StopRecording(string stationName)
        {
            string stationKey = stationName.ToLower();

            if (_activeProcesses.TryRemove(stationKey, out var proc))
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        // Gửi lệnh 'q' vào StandardInput để dừng nhẹ nhàng (giúp file mp4 không lỗi header)
                        try
                        {
                            using (var sw = proc.StandardInput)
                            {
                                if (sw.BaseStream.CanWrite)
                                    sw.WriteLine("q");
                            }
                        }
                        catch { }

                        // Chờ tối đa 1.5s, nếu không tắt thì Kill
                        if (!proc.WaitForExit(1500))
                        {
                            proc.Kill();
                        }
                    }

                    Console.WriteLine($"[INFO] Đã dừng ghi hình tại {stationName}");
                    return true;
                }
                catch
                {
                    try { proc.Kill(); } catch { }
                    return false;
                }
            }
            return false;
        }

        // Helper tìm ffmpeg.exe
        private string FindFfmpegPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string path1 = Path.Combine(baseDir, "ffmpeg.exe");
            if (File.Exists(path1)) return path1;

            string currentDir = Directory.GetCurrentDirectory();
            string path2 = Path.Combine(currentDir, "ffmpeg.exe");
            if (File.Exists(path2)) return path2;

            return null; // Không tìm thấy
        }
    }
}