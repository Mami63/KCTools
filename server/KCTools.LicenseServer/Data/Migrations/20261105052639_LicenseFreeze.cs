using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KCTools.LicenseServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class LicenseFreeze : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExtendedCount",
                table: "Licenses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FreezeReason",
                table: "Licenses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FrozenAt",
                table: "Licenses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFrozen",
                table: "Licenses",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtendedCount",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "FreezeReason",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "FrozenAt",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "IsFrozen",
                table: "Licenses");
        }
    }
}
