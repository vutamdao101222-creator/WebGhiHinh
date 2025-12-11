namespace WebGhiHinh.Services
{
    public static class UserQrParser
    {
        // Quy ước QR nhân viên:
        // EMP:<EmployeeCode>
        // Ví dụ: EMP:NV00123
        public static bool TryParseEmployeeCode(string? raw, out string employeeCode)
        {
            employeeCode = string.Empty;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            raw = raw.Trim();

            // Cho phép vài biến thể an toàn
            if (raw.StartsWith("EMP:", StringComparison.OrdinalIgnoreCase))
            {
                employeeCode = raw.Substring(4).Trim();
                return !string.IsNullOrWhiteSpace(employeeCode);
            }

            if (raw.StartsWith("EMP-", StringComparison.OrdinalIgnoreCase))
            {
                employeeCode = raw.Substring(4).Trim();
                return !string.IsNullOrWhiteSpace(employeeCode);
            }

            return false;
        }
    }
}
