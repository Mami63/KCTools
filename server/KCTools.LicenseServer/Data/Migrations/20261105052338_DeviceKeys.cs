using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace KCTools.LicenseServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class DeviceKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ActivatedAt",
                table: "Devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeviceId",
                table: "Devices",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "KeyThumbprint",
                table: "Devices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastProofAt",
                table: "Devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicKeyPem",
                table: "Devices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RevokedAt",
                table: "Devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevokedBy",
                table: "Devices",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ActivationHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Hwid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Actor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Details = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivationHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeviceChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    NonceHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceChallenges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_DeviceId",
                table: "Devices",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_KeyThumbprint",
                table: "Devices",
                column: "KeyThumbprint");

            migrationBuilder.CreateIndex(
                name: "IX_ActivationHistory_CreatedAt",
                table: "ActivationHistory",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ActivationHistory_LicenseId",
                table: "ActivationHistory",
                column: "LicenseId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceChallenges_ExpiresAt",
                table: "DeviceChallenges",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceChallenges_LicenseId_DeviceId_NonceHash",
                table: "DeviceChallenges",
                columns: new[] { "LicenseId", "DeviceId", "NonceHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivationHistory");

            migrationBuilder.DropTable(
                name: "DeviceChallenges");

            migrationBuilder.DropIndex(
                name: "IX_Devices_DeviceId",
                table: "Devices");

            migrationBuilder.DropIndex(
                name: "IX_Devices_KeyThumbprint",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "ActivatedAt",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "DeviceId",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "KeyThumbprint",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "LastProofAt",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "PublicKeyPem",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "RevokedAt",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "RevokedBy",
                table: "Devices");
        }
    }
}
