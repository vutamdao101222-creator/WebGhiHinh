using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebGhiHinh.Migrations
{
    /// <inheritdoc />
    public partial class ThemTruongDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Stations_Cameras_CameraId",
                table: "Stations");

            migrationBuilder.RenameColumn(
                name: "CameraId",
                table: "Stations",
                newName: "QrCameraId");

            migrationBuilder.RenameIndex(
                name: "IX_Stations_CameraId",
                table: "Stations",
                newName: "IX_Stations_QrCameraId");

            migrationBuilder.RenameColumn(
                name: "WebrtcName",
                table: "Cameras",
                newName: "Description");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Stations",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<int>(
                name: "OverviewCameraId",
                table: "Stations",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Cameras",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.CreateIndex(
                name: "IX_Stations_OverviewCameraId",
                table: "Stations",
                column: "OverviewCameraId");

            migrationBuilder.AddForeignKey(
                name: "FK_Stations_Cameras_OverviewCameraId",
                table: "Stations",
                column: "OverviewCameraId",
                principalTable: "Cameras",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Stations_Cameras_QrCameraId",
                table: "Stations",
                column: "QrCameraId",
                principalTable: "Cameras",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Stations_Cameras_OverviewCameraId",
                table: "Stations");

            migrationBuilder.DropForeignKey(
                name: "FK_Stations_Cameras_QrCameraId",
                table: "Stations");

            migrationBuilder.DropIndex(
                name: "IX_Stations_OverviewCameraId",
                table: "Stations");

            migrationBuilder.DropColumn(
                name: "OverviewCameraId",
                table: "Stations");

            migrationBuilder.RenameColumn(
                name: "QrCameraId",
                table: "Stations",
                newName: "CameraId");

            migrationBuilder.RenameIndex(
                name: "IX_Stations_QrCameraId",
                table: "Stations",
                newName: "IX_Stations_CameraId");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "Cameras",
                newName: "WebrtcName");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Stations",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Cameras",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddForeignKey(
                name: "FK_Stations_Cameras_CameraId",
                table: "Stations",
                column: "CameraId",
                principalTable: "Cameras",
                principalColumn: "Id");
        }
    }
}
