using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend_api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceName = table.Column<string>(type: "text", nullable: false),
                    Processor = table.Column<string>(type: "text", nullable: false),
                    ProcessorSpeed = table.Column<string>(type: "text", nullable: false),
                    InstalledRam = table.Column<string>(type: "text", nullable: false),
                    UsableRam = table.Column<string>(type: "text", nullable: false),
                    GraphicsCard = table.Column<string>(type: "text", nullable: false),
                    GraphicsMemory = table.Column<string>(type: "text", nullable: false),
                    TotalStorage = table.Column<string>(type: "text", nullable: false),
                    UsedStorage = table.Column<string>(type: "text", nullable: false),
                    FreeStorage = table.Column<string>(type: "text", nullable: false),
                    DeviceId = table.Column<string>(type: "text", nullable: false),
                    ProductId = table.Column<string>(type: "text", nullable: false),
                    SystemType = table.Column<string>(type: "text", nullable: false),
                    WindowsEdition = table.Column<string>(type: "text", nullable: false),
                    WindowsVersion = table.Column<string>(type: "text", nullable: false),
                    OsBuild = table.Column<string>(type: "text", nullable: false),
                    InstalledOn = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    LastSeenAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Devices_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeviceDrives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    DriveLetter = table.Column<string>(type: "text", nullable: false),
                    DriveType = table.Column<string>(type: "text", nullable: false),
                    FileSystem = table.Column<string>(type: "text", nullable: false),
                    VolumeLabel = table.Column<string>(type: "text", nullable: false),
                    TotalSize = table.Column<string>(type: "text", nullable: false),
                    UsedSpace = table.Column<string>(type: "text", nullable: false),
                    FreeSpace = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceDrives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceDrives_Devices_DeviceEntityId",
                        column: x => x.DeviceEntityId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceDrives_DeviceEntityId",
                table: "DeviceDrives",
                column: "DeviceEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_UserId_DeviceId",
                table: "Devices",
                columns: new[] { "UserId", "DeviceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceDrives");

            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
