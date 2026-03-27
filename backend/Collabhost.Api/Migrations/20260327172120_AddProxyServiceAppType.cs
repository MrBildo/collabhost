using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Collabhost.Api.Migrations;

/// <inheritdoc />
public partial class AddProxyServiceAppType : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProcessState",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProcessState", x => x.Id);
            });

        migrationBuilder.InsertData(
            table: "AppType",
            columns: ["Id", "Description", "DisplayName", "IsActive", "Name", "Ordinal"],
            values: [new Guid("76a3e5b7-d751-4eba-aac2-b1c26c1f9ca3"), null, "Proxy Service", true, "ProxyService", 3]);

        migrationBuilder.InsertData(
            table: "ProcessState",
            columns: ["Id", "Description", "DisplayName", "IsActive", "Name", "Ordinal"],
            values: new object[,]
            {
                { new Guid("05f6e7d8-394a-1234-f012-345678901234"), null, "Restarting", true, "Restarting", 5 },
                { new Guid("b0a1c2d3-e4f5-6789-abcd-ef0123456789"), null, "Stopped", true, "Stopped", 0 },
                { new Guid("c1b2a3d4-f5e6-7890-bcde-f01234567890"), null, "Starting", true, "Starting", 1 },
                { new Guid("d2c3b4a5-0617-8901-cdef-012345678901"), null, "Running", true, "Running", 2 },
                { new Guid("e3d4c5b6-1728-9012-def0-123456789012"), null, "Stopping", true, "Stopping", 3 },
                { new Guid("f4e5d6c7-2839-0123-ef01-234567890123"), null, "Crashed", true, "Crashed", 4 }
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ProcessState");

        migrationBuilder.DeleteData(
            table: "AppType",
            keyColumn: "Id",
            keyValue: new Guid("76a3e5b7-d751-4eba-aac2-b1c26c1f9ca3"));
    }
}
