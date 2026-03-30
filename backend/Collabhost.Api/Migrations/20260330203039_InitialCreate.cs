using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional
#pragma warning disable IDE0300 // Collection initialization can be simplified — generated code

namespace Collabhost.Api.Migrations
{
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
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    AppTypeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstallDirectory = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: true),
                    IsStopped = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
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
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsBuiltIn = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppType", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Capability",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Capability", x => x.Id);
                });

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

            migrationBuilder.CreateTable(
                name: "AppTypeCapability",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AppTypeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CapabilityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Configuration = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppTypeCapability", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppTypeCapability_AppType_AppTypeId",
                        column: x => x.AppTypeId,
                        principalTable: "AppType",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AppTypeCapability_Capability_CapabilityId",
                        column: x => x.CapabilityId,
                        principalTable: "Capability",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CapabilityConfiguration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AppId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AppTypeCapabilityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Configuration = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapabilityConfiguration", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CapabilityConfiguration_AppTypeCapability_AppTypeCapabilityId",
                        column: x => x.AppTypeCapabilityId,
                        principalTable: "AppTypeCapability",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CapabilityConfiguration_App_AppId",
                        column: x => x.AppId,
                        principalTable: "App",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "AppType",
                columns: new[] { "Id", "Description", "DisplayName", "ExternalId", "IsBuiltIn", "Name" },
                values: new object[,]
                {
                    { new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456001"), ".NET web application hosted via Kestrel", "ASP.NET Core", "01JSEED00000DOTNETAPP0001", true, "dotnet-app" },
                    { new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456002"), "Node.js application", "Node.js", "01JSEED00000NODEAPP00001", true, "node-app" },
                    { new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456003"), "Generic executable process", "Executable", "01JSEED00000EXECUTABLE01", true, "executable" },
                    { new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456004"), "React single-page application served as static files", "React App", "01JSEED00000REACTAPP0001", true, "react-app" },
                    { new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456005"), "Static files served directly by the reverse proxy", "Static Site", "01JSEED00000STATICSITE01", true, "static-site" }
                });

            migrationBuilder.InsertData(
                table: "Capability",
                columns: new[] { "Id", "Category", "Description", "DisplayName", "Slug" },
                values: new object[,]
                {
                    { new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560001"), "behavioral", "How the app's process is discovered, started, and stopped", "Process Management", "process" },
                    { new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560002"), "behavioral", "How the platform communicates the assigned port to the app process", "Port Injection", "port-injection" },
                    { new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560003"), "behavioral", "How traffic reaches the app through the reverse proxy", "Routing", "routing" },
                    { new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560004"), "behavioral", "HTTP endpoint polled to determine app health", "Health Check", "health-check" },
                    { new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560005"), "behavioral", "Environment variables injected when the app process starts", "Environment Variables", "environment-defaults" },
                    { new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560006"), "behavioral", "How the platform responds when the app process exits unexpectedly", "Restart Policy", "restart" },
                    { new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560007"), "behavioral", "Whether the app starts automatically when Collabhost starts", "Auto Start", "auto-start" },
                    { new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560008"), "informational", ".NET runtime and framework version information", "ASP.NET Runtime", "aspnet-runtime" },
                    { new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560009"), "informational", "Node.js version and package manager information", "Node.js Runtime", "node-runtime" },
                    { new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560010"), "informational", "React framework and tooling information", "React", "react-runtime" }
                });

            migrationBuilder.InsertData(
                table: "ProcessState",
                columns: new[] { "Id", "Description", "DisplayName", "IsActive", "Name", "Ordinal" },
                values: new object[,]
                {
                    { new Guid("05f6e7d8-394a-1234-f012-345678901234"), null, "Restarting", true, "Restarting", 5 },
                    { new Guid("b0a1c2d3-e4f5-6789-abcd-ef0123456789"), null, "Stopped", true, "Stopped", 0 },
                    { new Guid("c1b2a3d4-f5e6-7890-bcde-f01234567890"), null, "Starting", true, "Starting", 1 },
                    { new Guid("d2c3b4a5-0617-8901-cdef-012345678901"), null, "Running", true, "Running", 2 },
                    { new Guid("e3d4c5b6-1728-9012-def0-123456789012"), null, "Stopping", true, "Stopping", 3 },
                    { new Guid("f4e5d6c7-2839-0123-ef01-234567890123"), null, "Crashed", true, "Crashed", 4 }
                });

            migrationBuilder.InsertData(
                table: "AppTypeCapability",
                columns: new[] { "Id", "AppTypeId", "CapabilityId", "Configuration" },
                values: new object[,]
                {
                    { new Guid("d0d0d0d0-aeed-4000-a000-000000000001"), new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456001"), new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560001"), "{\"discoveryStrategy\":\"dotnet-runtimeconfig\",\"gracefulShutdown\":true,\"shutdownTimeoutSeconds\":30}" },
                    { new Guid("d0d0d0d0-aeed-4000-a000-000000000002"), new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456001"), new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560002"), "{\"envVar\":\"ASPNETCORE_URLS\",\"format\":\"http://localhost:{port}\"}" },
                    { new Guid("d0d0d0d0-aeed-4000-a000-000000000003"), new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456001"), new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560003"), "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"reverseProxy\"}" },
                    { new Guid("d0d0d0d0-aeed-4000-a000-000000000004"), new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456001"), new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560004"), "{\"endpoint\":\"/health\",\"intervalSeconds\":30,\"timeoutSeconds\":5,\"retries\":3}" },
                    { new Guid("d0d0d0d0-aeed-4000-a000-000000000005"), new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456001"), new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560005"), "{\"defaults\":{\"ASPNETCORE_ENVIRONMENT\":\"Production\"}}" },
                    { new Guid("d0d0d0d0-aeed-4000-a000-000000000006"), new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456001"), new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560006"), "{\"policy\":\"always\"}" },
                    { new Guid("d0d0d0d0-aeed-4000-a000-000000000007"), new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456001"), new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560007"), "{\"enabled\":true}" },
                    { new Guid("d0d0d0d0-aeed-4000-a000-000000000008"), new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456001"), new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560008"), "{\"targetFramework\":\"net10.0\",\"runtimeVersion\":\"10.0.x\",\"selfContained\":false}" },
                    { new Guid("d0d0d0d0-aeed-4000-a000-000000000009"), new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456002"), new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560001"), "{\"discoveryStrategy\":\"package-json\",\"gracefulShutdown\":true,\"shutdownTimeoutSeconds\":30}" },
                    { new Guid("d0d0d0d0-aeed-4000-a000-00000000000a"), new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456002"), new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560002"), "{\"envVar\":\"PORT\",\"format\":\"{port}\"}" },
                    { new Guid("d0d0d0d0-aeed-4000-a000-00000000000b"), new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456002"), new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560003"), "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"reverseProxy\"}" },
                    { new Guid("d0d0d0d0-aeed-4000-a000-00000000000c"), new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456002"), new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560004"), "{\"endpoint\":\"/health\",\"intervalSeconds\":30,\"timeoutSeconds\":5,\"retries\":3}" },
                    { new Guid("d0d0d0d0-aeed-4000-a000-00000000000d"), new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456002"), new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560006"), "{\"policy\":\"always\"}" },
                    { new Guid("d0d0d0d0-aeed-4000-a000-00000000000e"), new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456002"), new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560007"), "{\"enabled\":true}" },
                    { new Guid("d0d0d0d0-aeed-4000-a000-00000000000f"), new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456002"), new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560009"), "{\"nodeVersion\":\"22.x\",\"packageManager\":\"npm\"}" },
                    { new Guid("d0d0d0d0-aeed-4000-a000-000000000010"), new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456003"), new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560001"), "{\"discoveryStrategy\":\"manual\",\"gracefulShutdown\":false,\"shutdownTimeoutSeconds\":10}" },
                    { new Guid("d0d0d0d0-aeed-4000-a000-000000000011"), new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456003"), new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560002"), "{\"envVar\":\"PORT\",\"format\":\"{port}\"}" },
                    { new Guid("d0d0d0d0-aeed-4000-a000-000000000012"), new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456003"), new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560003"), "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"reverseProxy\"}" },
                    { new Guid("d0d0d0d0-aeed-4000-a000-000000000013"), new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456003"), new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560006"), "{\"policy\":\"onCrash\"}" },
                    { new Guid("d0d0d0d0-aeed-4000-a000-000000000014"), new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456003"), new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560007"), "{\"enabled\":false}" },
                    { new Guid("d0d0d0d0-aeed-4000-a000-000000000015"), new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456004"), new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560003"), "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"fileServer\",\"spaFallback\":true}" },
                    { new Guid("d0d0d0d0-aeed-4000-a000-000000000016"), new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456004"), new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560009"), "{\"nodeVersion\":\"22.x\",\"packageManager\":\"npm\",\"buildCommand\":\"npm run build\"}" },
                    { new Guid("d0d0d0d0-aeed-4000-a000-000000000017"), new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456004"), new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560010"), "{\"version\":\"18.x\",\"router\":\"react-router\",\"bundler\":\"vite\"}" },
                    { new Guid("d0d0d0d0-aeed-4000-a000-000000000018"), new Guid("b1a2c3d4-e5f6-7890-abcd-ef0123456005"), new Guid("c2b3a4d5-f6e7-8901-bcde-f01234560003"), "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"fileServer\",\"spaFallback\":false}" }
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
                name: "IX_AppType_ExternalId",
                table: "AppType",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppType_Name",
                table: "AppType",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppTypeCapability_AppTypeId_CapabilityId",
                table: "AppTypeCapability",
                columns: new[] { "AppTypeId", "CapabilityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppTypeCapability_CapabilityId",
                table: "AppTypeCapability",
                column: "CapabilityId");

            migrationBuilder.CreateIndex(
                name: "IX_Capability_Slug",
                table: "Capability",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CapabilityConfiguration_AppId_AppTypeCapabilityId",
                table: "CapabilityConfiguration",
                columns: new[] { "AppId", "AppTypeCapabilityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CapabilityConfiguration_AppTypeCapabilityId",
                table: "CapabilityConfiguration",
                column: "AppTypeCapabilityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CapabilityConfiguration");

            migrationBuilder.DropTable(
                name: "ProcessState");

            migrationBuilder.DropTable(
                name: "AppTypeCapability");

            migrationBuilder.DropTable(
                name: "App");

            migrationBuilder.DropTable(
                name: "AppType");

            migrationBuilder.DropTable(
                name: "Capability");
        }
    }
}
