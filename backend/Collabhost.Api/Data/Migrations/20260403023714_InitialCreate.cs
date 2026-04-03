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
                    { "01KN8K1MRQ0K06ADYJJ8VAXG5Y", new DateTime(2026, 4, 3, 2, 37, 13, 803, DateTimeKind.Utc).AddTicks(5489), "ASP.NET Core or .NET console application", ".NET Application", true, "{\"runtime\":{\"name\":\".NET\",\"version\":\"10\",\"targetFramework\":\"net10.0\"}}", "dotnet-app" },
                    { "01KN8K1MRT26VCX65J1ZSVWESB", new DateTime(2026, 4, 3, 2, 37, 13, 804, DateTimeKind.Utc).AddTicks(2127), "Static files served by Caddy", "Static Site", true, null, "static-site" },
                    { "01KN8K1MRT4XGXXW5BBQ8YZNN2", new DateTime(2026, 4, 3, 2, 37, 13, 804, DateTimeKind.Utc).AddTicks(2115), "Server-side JavaScript with npm", "Node.js Application", true, "{\"runtime\":{\"name\":\"Node.js\",\"version\":\"22\",\"packageManager\":\"npm\"}}", "nodejs-app" }
                });

            migrationBuilder.InsertData(
                table: "CapabilityBindings",
                columns: new[] { "Id", "AppTypeId", "CapabilitySlug", "DefaultConfigurationJson" },
                values: new object[,]
                {
                    { "01KN8K1MRT0B5TF2TTXV68DAFJ", "01KN8K1MRQ0K06ADYJJ8VAXG5Y", "environment-defaults", "{\"variables\":{\"ASPNETCORE_ENVIRONMENT\":\"Production\"}}" },
                    { "01KN8K1MRT1SW33ZS6DK4TTGKB", "01KN8K1MRQ0K06ADYJJ8VAXG5Y", "auto-start", "{\"enabled\":true}" },
                    { "01KN8K1MRT34CN63B8QZ96N3Q7", "01KN8K1MRQ0K06ADYJJ8VAXG5Y", "routing", "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"ReverseProxy\",\"spaFallback\":false}" },
                    { "01KN8K1MRT9D970Y0XZR74W1Z1", "01KN8K1MRT4XGXXW5BBQ8YZNN2", "restart", "{\"policy\":\"OnCrash\"}" },
                    { "01KN8K1MRT9RES6FCWSNFYNXGK", "01KN8K1MRT4XGXXW5BBQ8YZNN2", "process", "{\"discoveryStrategy\":\"PackageJson\",\"gracefulShutdown\":true,\"shutdownTimeoutSeconds\":15}" },
                    { "01KN8K1MRTCFS85XS4TRW6EGSR", "01KN8K1MRQ0K06ADYJJ8VAXG5Y", "port-injection", "{\"environmentVariableName\":\"ASPNETCORE_URLS\",\"portFormat\":\"http://localhost:{port}\"}" },
                    { "01KN8K1MRTD4TJKKDPGHG36Z4K", "01KN8K1MRT4XGXXW5BBQ8YZNN2", "port-injection", "{\"environmentVariableName\":\"PORT\",\"portFormat\":\"{port}\"}" },
                    { "01KN8K1MRTE14RGEAS4VDD44P3", "01KN8K1MRT26VCX65J1ZSVWESB", "artifact", "{\"location\":\"\"}" },
                    { "01KN8K1MRTEFCM6C0ZXM6GFM68", "01KN8K1MRT4XGXXW5BBQ8YZNN2", "health-check", "{\"endpoint\":\"/health\",\"intervalSeconds\":30,\"timeoutSeconds\":5}" },
                    { "01KN8K1MRTETFY88Z8FTJGCBB5", "01KN8K1MRT26VCX65J1ZSVWESB", "routing", "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"FileServer\",\"spaFallback\":true}" },
                    { "01KN8K1MRTG49PHRKY1N3DMFKN", "01KN8K1MRQ0K06ADYJJ8VAXG5Y", "health-check", "{\"endpoint\":\"/health\",\"intervalSeconds\":30,\"timeoutSeconds\":5}" },
                    { "01KN8K1MRTGF8GQ3X2CFS2JCQS", "01KN8K1MRQ0K06ADYJJ8VAXG5Y", "restart", "{\"policy\":\"OnCrash\"}" },
                    { "01KN8K1MRTGPRVSG3F6EJBB8CM", "01KN8K1MRT4XGXXW5BBQ8YZNN2", "routing", "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"ReverseProxy\",\"spaFallback\":false}" },
                    { "01KN8K1MRTHFBR5P75WEE5K3NT", "01KN8K1MRT4XGXXW5BBQ8YZNN2", "artifact", "{\"location\":\"\"}" },
                    { "01KN8K1MRTJD1NCG0J9R4364MJ", "01KN8K1MRQ0K06ADYJJ8VAXG5Y", "process", "{\"discoveryStrategy\":\"DotNetRuntimeConfiguration\",\"gracefulShutdown\":true,\"shutdownTimeoutSeconds\":30}" },
                    { "01KN8K1MRTP60DVWP6ERZ8R4F9", "01KN8K1MRQ0K06ADYJJ8VAXG5Y", "artifact", "{\"location\":\"\"}" },
                    { "01KN8K1MRTT086R433KGMBT21A", "01KN8K1MRT4XGXXW5BBQ8YZNN2", "auto-start", "{\"enabled\":true}" },
                    { "01KN8K1MRTZZ9PJ1QMZSG5QHRE", "01KN8K1MRT4XGXXW5BBQ8YZNN2", "environment-defaults", "{\"variables\":{\"NODE_ENV\":\"production\"}}" }
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
