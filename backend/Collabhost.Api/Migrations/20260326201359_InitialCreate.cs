using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Collabhost.Api.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "App",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                AppTypeId = table.Column<Guid>(type: "TEXT", nullable: false),
                InstallDirectory = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                CommandLine = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                Arguments = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                WorkingDirectory = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                RestartPolicyId = table.Column<Guid>(type: "TEXT", nullable: false),
                Port = table.Column<int>(type: "INTEGER", nullable: true),
                HealthEndpoint = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                UpdateCommand = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                AutoStart = table.Column<bool>(type: "INTEGER", nullable: false),
                RegisteredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                ExternalId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_App", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "AppType",
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
                table.PrimaryKey("PK_AppType", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "RestartPolicy",
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
                table.PrimaryKey("PK_RestartPolicy", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "EnvironmentVariable",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                AppId = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Value = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EnvironmentVariable", x => x.Id);
                table.ForeignKey(
                    name: "FK_EnvironmentVariable_App_AppId",
                    column: x => x.AppId,
                    principalTable: "App",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.InsertData(
            table: "AppType",
            columns: ["Id", "Description", "DisplayName", "IsActive", "Name", "Ordinal"],
            values: new object[,]
            {
                { new Guid("7dc8cc9f-1600-447a-85f4-cbc0fc44e6fc"), null, "Static Site", true, "StaticSite", 2 },
                { new Guid("acdb6994-2c22-42f5-bf89-68c42c9f980c"), null, "Executable", true, "Executable", 0 },
                { new Guid("d71d5599-bad3-4b28-8920-1aae916bd3cb"), null, "NPM Package", true, "NpmPackage", 1 }
            });

        migrationBuilder.InsertData(
            table: "RestartPolicy",
            columns: ["Id", "Description", "DisplayName", "IsActive", "Name", "Ordinal"],
            values: new object[,]
            {
                { new Guid("2f2f6115-b6ef-4db4-b3c7-200a4dbb3408"), null, "Never", true, "Never", 0 },
                { new Guid("3902811f-674d-483a-9d6b-8b8917d83c0f"), null, "Always", true, "Always", 2 },
                { new Guid("a5806eba-9dcd-4145-acc3-7bcabd699829"), null, "On Crash", true, "OnCrash", 1 }
            });

        migrationBuilder.CreateIndex(
            name: "IX_App_ExternalId",
            table: "App",
            column: "ExternalId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_App_Name",
            table: "App",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_EnvironmentVariable_AppId_Name",
            table: "EnvironmentVariable",
            columns: ["AppId", "Name"],
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AppType");

        migrationBuilder.DropTable(
            name: "EnvironmentVariable");

        migrationBuilder.DropTable(
            name: "RestartPolicy");

        migrationBuilder.DropTable(
            name: "App");
    }
}
