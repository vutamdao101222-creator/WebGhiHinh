// ===============================
// FILE: Models/Dto/StationDto.cs
// ===============================
namespace WebGhiHinh.Models.Dto
{
    public sealed class CameraMiniDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string RtspUrl { get; set; } = "";
        public string? Description { get; set; }
    }

    public sealed class StationDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";

        public int? CurrentUserId { get; set; }
        public string? CurrentUsername { get; set; }

        public int? OverviewCameraId { get; set; }
        public CameraMiniDto? OverviewCamera { get; set; }

        public int? QrCameraId { get; set; }
        public CameraMiniDto? QrCamera { get; set; }
    }

    public sealed class SetStationCamerasRequest
    {
        public int StationId { get; set; }
        public int? OverviewCameraId { get; set; }
        public int? QrCameraId { get; set; }
    }
}
