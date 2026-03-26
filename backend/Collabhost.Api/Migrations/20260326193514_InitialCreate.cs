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
            name: "Apps",
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
                table.PrimaryKey("PK_Apps", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "AppTypes",
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
                table.PrimaryKey("PK_AppTypes", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "RestartPolicies",
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
                table.PrimaryKey("PK_RestartPolicies", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "EnvironmentVariables",
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
                table.PrimaryKey("PK_EnvironmentVariables", x => x.Id);
                table.ForeignKey(
                    name: "FK_EnvironmentVariables_Apps_AppId",
                    column: x => x.AppId,
                    principalTable: "Apps",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.InsertData(
            table: "AppTypes",
            columns: ["Id", "Description", "DisplayName", "IsActive", "Name", "Ordinal"],
            values: new object[,]
            {
                { new Guid("a1b2c3d4-0001-0001-0001-000000000001"), null, "Executable", true, "Executable", 0 },
                { new Guid("a1b2c3d4-0001-0001-0001-000000000002"), null, "NPM Package", true, "NpmPackage", 1 },
                { new Guid("a1b2c3d4-0001-0001-0001-000000000003"), null, "Static Site", true, "StaticSite", 2 }
            });

        migrationBuilder.InsertData(
            table: "RestartPolicies",
            columns: ["Id", "Description", "DisplayName", "IsActive", "Name", "Ordinal"],
            values: new object[,]
            {
                { new Guid("a1b2c3d4-0002-0002-0002-000000000001"), null, "Never", true, "Never", 0 },
                { new Guid("a1b2c3d4-0002-0002-0002-000000000002"), null, "On Crash", true, "OnCrash", 1 },
                { new Guid("a1b2c3d4-0002-0002-0002-000000000003"), null, "Always", true, "Always", 2 }
            });

        migrationBuilder.CreateIndex(
            name: "IX_Apps_ExternalId",
            table: "Apps",
            column: "ExternalId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Apps_Name",
            table: "Apps",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_EnvironmentVariables_AppId_Name",
            table: "EnvironmentVariables",
            columns: ["AppId", "Name"],
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AppTypes");

        migrationBuilder.DropTable(
            name: "EnvironmentVariables");

        migrationBuilder.DropTable(
            name: "RestartPolicies");

        migrationBuilder.DropTable(
            name: "Apps");
    }
}
