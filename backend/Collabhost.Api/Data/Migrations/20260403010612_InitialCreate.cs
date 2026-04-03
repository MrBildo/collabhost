using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Collabhost.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppTypes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsBuiltIn = table.Column<bool>(type: "INTEGER", nullable: false),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Apps",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AppTypeId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    RegisteredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Apps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Apps_AppTypes_AppTypeId",
                        column: x => x.AppTypeId,
                        principalTable: "AppTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CapabilityBindings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    AppTypeId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    CapabilitySlug = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DefaultConfigurationJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapabilityBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CapabilityBindings_AppTypes_AppTypeId",
                        column: x => x.AppTypeId,
                        principalTable: "AppTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CapabilityOverrides",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    AppId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    CapabilitySlug = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ConfigurationJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapabilityOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CapabilityOverrides_Apps_AppId",
                        column: x => x.AppId,
                        principalTable: "Apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "AppTypes",
                columns: new[] { "Id", "CreatedAt", "Description", "DisplayName", "IsBuiltIn", "MetadataJson", "Slug" },
                values: new object[,]
                {
                    { "01JQDZ000000000000000DNETA", new DateTime(2026, 4, 3, 1, 6, 12, 198, DateTimeKind.Utc).AddTicks(6729), "ASP.NET Core or .NET console application", ".NET Application", true, "{\"runtime\":{\"name\":\".NET\",\"version\":\"10\",\"targetFramework\":\"net10.0\"}}", "dotnet-app" },
                    { "01JQDZ000000000000000QZDEA", new DateTime(2026, 4, 3, 1, 6, 12, 199, DateTimeKind.Utc).AddTicks(3222), "Server-side JavaScript with npm", "Node.js Application", true, "{\"runtime\":{\"name\":\"Node.js\",\"version\":\"22\",\"packageManager\":\"npm\"}}", "nodejs-app" },
                    { "01JQDZ000000000000000STATA", new DateTime(2026, 4, 3, 1, 6, 12, 199, DateTimeKind.Utc).AddTicks(3229), "Static files served by Caddy", "Static Site", true, null, "static-site" }
                });

            migrationBuilder.InsertData(
                table: "CapabilityBindings",
                columns: new[] { "Id", "AppTypeId", "CapabilitySlug", "DefaultConfigurationJson" },
                values: new object[,]
                {
                    { "01JQDZ00000000000DNETBND01", "01JQDZ000000000000000DNETA", "artifact", "{\"location\":\"\"}" },
                    { "01JQDZ00000000000DNETBND02", "01JQDZ000000000000000DNETA", "process", "{\"discoveryStrategy\":\"DotNetRuntimeConfiguration\",\"gracefulShutdown\":true,\"shutdownTimeoutSeconds\":30}" },
                    { "01JQDZ00000000000DNETBND03", "01JQDZ000000000000000DNETA", "port-injection", "{\"environmentVariableName\":\"ASPNETCORE_URLS\",\"portFormat\":\"http://localhost:{port}\"}" },
                    { "01JQDZ00000000000DNETBND04", "01JQDZ000000000000000DNETA", "routing", "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"ReverseProxy\",\"spaFallback\":false}" },
                    { "01JQDZ00000000000DNETBND05", "01JQDZ000000000000000DNETA", "health-check", "{\"endpoint\":\"/health\",\"intervalSeconds\":30,\"timeoutSeconds\":5}" },
                    { "01JQDZ00000000000DNETBND06", "01JQDZ000000000000000DNETA", "restart", "{\"policy\":\"OnCrash\"}" },
                    { "01JQDZ00000000000DNETBND07", "01JQDZ000000000000000DNETA", "auto-start", "{\"enabled\":true}" },
                    { "01JQDZ00000000000DNETBND08", "01JQDZ000000000000000DNETA", "environment-defaults", "{\"variables\":{\"ASPNETCORE_ENVIRONMENT\":\"Production\"}}" },
                    { "01JQDZ00000000000NZDEBND01", "01JQDZ000000000000000QZDEA", "artifact", "{\"location\":\"\"}" },
                    { "01JQDZ00000000000NZDEBND02", "01JQDZ000000000000000QZDEA", "process", "{\"discoveryStrategy\":\"PackageJson\",\"gracefulShutdown\":true,\"shutdownTimeoutSeconds\":15}" },
                    { "01JQDZ00000000000NZDEBND03", "01JQDZ000000000000000QZDEA", "port-injection", "{\"environmentVariableName\":\"PORT\",\"portFormat\":\"{port}\"}" },
                    { "01JQDZ00000000000NZDEBND04", "01JQDZ000000000000000QZDEA", "routing", "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"ReverseProxy\",\"spaFallback\":false}" },
                    { "01JQDZ00000000000NZDEBND05", "01JQDZ000000000000000QZDEA", "health-check", "{\"endpoint\":\"/health\",\"intervalSeconds\":30,\"timeoutSeconds\":5}" },
                    { "01JQDZ00000000000NZDEBND06", "01JQDZ000000000000000QZDEA", "restart", "{\"policy\":\"OnCrash\"}" },
                    { "01JQDZ00000000000NZDEBND07", "01JQDZ000000000000000QZDEA", "auto-start", "{\"enabled\":true}" },
                    { "01JQDZ00000000000NZDEBND08", "01JQDZ000000000000000QZDEA", "environment-defaults", "{\"variables\":{\"NODE_ENV\":\"production\"}}" },
                    { "01JQDZ00000000000STATBND01", "01JQDZ000000000000000STATA", "artifact", "{\"location\":\"\"}" },
                    { "01JQDZ00000000000STATBND02", "01JQDZ000000000000000STATA", "routing", "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"FileServer\",\"spaFallback\":true}" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Apps_AppTypeId",
                table: "Apps",
                column: "AppTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Apps_Slug",
                table: "Apps",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppTypes_Slug",
                table: "AppTypes",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CapabilityBindings_AppTypeId_CapabilitySlug",
                table: "CapabilityBindings",
                columns: new[] { "AppTypeId", "CapabilitySlug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CapabilityOverrides_AppId_CapabilitySlug",
                table: "CapabilityOverrides",
                columns: new[] { "AppId", "CapabilitySlug" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CapabilityBindings");

            migrationBuilder.DropTable(
                name: "CapabilityOverrides");

            migrationBuilder.DropTable(
                name: "Apps");

            migrationBuilder.DropTable(
                name: "AppTypes");
        }
    }
}
