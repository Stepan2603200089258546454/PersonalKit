using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHome.Identity.DataContext.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Security_LoginAudit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeviceName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ClientId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ApplicationName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Security_LoginAudit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Security_UserDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    KeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Browser = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OperatingSystem = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeviceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    FirstIpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastIpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastLocation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsBlocked = table.Column<bool>(type: "boolean", nullable: false),
                    BlockedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    BlockedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    BlockReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Security_UserDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Security_UserSettings",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    DevicePolicy = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Security_UserSettings", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "Security_UserSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ApplicationName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    RevokeReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Security_UserSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Security_UserSessions_Security_UserDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Security_UserDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Security_SessionApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ApplicationName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Scopes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Security_SessionApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Security_SessionApplications_Security_UserSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Security_UserSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Security_LoginAudit_OccurredAt",
                table: "Security_LoginAudit",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_Security_LoginAudit_SessionId",
                table: "Security_LoginAudit",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Security_LoginAudit_UserId_OccurredAt",
                table: "Security_LoginAudit",
                columns: new[] { "UserId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Security_SessionApplications_SessionId_ClientId",
                table: "Security_SessionApplications",
                columns: new[] { "SessionId", "ClientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Security_UserDevices_UserId_IsBlocked",
                table: "Security_UserDevices",
                columns: new[] { "UserId", "IsBlocked" });

            migrationBuilder.CreateIndex(
                name: "IX_Security_UserDevices_UserId_KeyHash",
                table: "Security_UserDevices",
                columns: new[] { "UserId", "KeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Security_UserSessions_DeviceId",
                table: "Security_UserSessions",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_Security_UserSessions_UserId_RevokedAt",
                table: "Security_UserSessions",
                columns: new[] { "UserId", "RevokedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Security_LoginAudit");

            migrationBuilder.DropTable(
                name: "Security_SessionApplications");

            migrationBuilder.DropTable(
                name: "Security_UserSettings");

            migrationBuilder.DropTable(
                name: "Security_UserSessions");

            migrationBuilder.DropTable(
                name: "Security_UserDevices");
        }
    }
}
