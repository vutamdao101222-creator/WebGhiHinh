using System.Diagnostics;
using System.Collections.Concurrent;

namespace WebGhiHinh.Services
{
    public class FfmpegService
    {
        // Key = stationName → mỗi trạm chỉ có 1 tiến trình FFmpeg
        private static readonly ConcurrentDictionary<string, Process> _activeProcesses = new();

        private readonly string _outputFolder = @"C:\GhiHinhVideos";

        public FfmpegService()
        {
            if (!Directory.Exists(_outputFolder))
                Directory.CreateDirectory(_outputFolder);
        }

        public string StartRecording(string rtspUrl, string qrCode, string stationName, string userName)
        {
            string stationKey = stationName.ToLower();

            // Nếu trạm này đang ghi → dừng lại
            if (_activeProcesses.ContainsKey(stationKey))
            {
                StopRecording(stationName);
            }

            string stationFolder = Path.Combine(_outputFolder, stationName);
            Directory.CreateDirectory(stationFolder);

            string safeQr = qrCode.Replace("/", "_").Replace("\\", "_").Replace(":", "");
            string safeUser = userName.Replace("/", "_").Replace("\\", "_");
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            string fileName = $"{safeUser}_{safeQr}_{timestamp}.mp4";
            string fullPath = Path.Combine(stationFolder, fileName);

            string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            if (!File.Exists(ffmpegPath))
                ffmpegPath = Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg.exe");

            string arguments =
                $"-y -rtsp_transport tcp -i \"{rtspUrl}\" " +
                $"-c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac " +
                $"\"{fullPath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                RedirectStandardInput = true,      // FIX — quan trọng nhất!
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                Process proc = new Process { StartInfo = psi };
                proc.Start();
                proc.BeginErrorReadLine();

                _activeProcesses.TryAdd(stationKey, proc);

                Console.WriteLine($"[INFO] Recording started at {stationName}: {fileName}");

                return Path.Combine(stationName, fileName).Replace("\\", "/");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Cannot start FFmpeg: {ex.Message}");
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
                        try
                        {
                            using (var sw = proc.StandardInput)
                            {
                                if (sw.BaseStream.CanWrite)
                                    sw.WriteLine("q");    // SAFE close
                            }
                        }
                        catch { }

                        if (!proc.WaitForExit(1500))
                            proc.Kill();
                    }

                    Console.WriteLine($"[INFO] Recording stopped at {stationName}");
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
    }
}
