using System.Text.Json;

namespace WebGhiHinh.Services
{
    public class SystemSettingsService
    {
        private readonly string _filePath = "system_settings.json";

        // Mặc định 30 ngày
        public int RetentionDays { get; private set; } = 30;

        public SystemSettingsService()
        {
            LoadSettings();
        }

        public void SaveSettings(int days)
        {
            RetentionDays = days;
            var json = JsonSerializer.Serialize(new { RetentionDays = days });
            File.WriteAllText(_filePath, json);
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    var data = JsonSerializer.Deserialize<JsonElement>(json);
                    if (data.TryGetProperty("RetentionDays", out var daysElement))
                    {
                        RetentionDays = daysElement.GetInt32();
                    }
                }
            }
            catch
            {
                // Nếu lỗi thì giữ mặc định 30
                RetentionDays = 30;
            }
        }
    }
}