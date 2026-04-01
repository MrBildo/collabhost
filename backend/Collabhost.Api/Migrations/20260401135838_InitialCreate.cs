using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional
#pragma warning disable IDE0300 // Collection initialization can be simplified

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
                name: "DiscoveryStrategy",
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
                    table.PrimaryKey("PK_DiscoveryStrategy", x => x.Id);
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
                name: "ServeMode",
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
                    table.PrimaryKey("PK_ServeMode", x => x.Id);
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
                    { new Guid("49d21824-f9e6-4a44-9b12-130f8c680cb9"), "Node.js application", "Node.js", "01KN0P7JYNRBD8DC9DMKEDJX2M", true, "node-app" },
                    { new Guid("56608f77-aa9d-44b7-a3cb-df7c361d8fb8"), "System process with management but no routing", "System Service", "01KN4N7APRQJQ8WG6NGZ1Y98TY", true, "system-service" },
                    { new Guid("606cdf1f-f41e-42d2-bb13-04b598de0f63"), "Static files served directly by the reverse proxy", "Static Site", "01KN0P7JYN9TDB3SPPS25Z493F", true, "static-site" },
                    { new Guid("73e28a95-764f-4ae5-9c2b-a9fdea66c348"), "React single-page application served as static files", "React App", "01KN0P7JYNM6PJP07XTAXK77GR", true, "react-app" },
                    { new Guid("bf5105c8-6a99-414c-96b6-c74aab5471f7"), "Generic executable process", "Executable", "01KN0P7JYNJRAHGC01N17NFTWW", true, "executable" },
                    { new Guid("d3333aba-642e-4784-a501-856a25ae6fe5"), ".NET web application hosted via Kestrel", "ASP.NET Core", "01KN0P7JYNYACWC35R77C1KTV2", true, "dotnet-app" }
                });

            migrationBuilder.InsertData(
                table: "Capability",
                columns: new[] { "Id", "Category", "Description", "DisplayName", "Slug" },
                values: new object[,]
                {
                    { new Guid("0ba21247-bf12-4487-b2ad-e4c84a784d75"), "informational", "Node.js version and package manager information", "Node.js Runtime", "node-runtime" },
                    { new Guid("0c66bd0a-d6b1-419d-83c2-9186d773cc52"), "behavioral", "How the platform communicates the assigned port to the app process", "Port Injection", "port-injection" },
                    { new Guid("1f642072-7975-4fa0-8109-4d9be5ffa909"), "informational", ".NET runtime and framework version information", "ASP.NET Runtime", "aspnet-runtime" },
                    { new Guid("2661f347-6af3-40a6-a0c4-57fb7e4d1f72"), "behavioral", "How the platform responds when the app process exits unexpectedly", "Restart Policy", "restart" },
                    { new Guid("2bc51a48-5e56-4c27-958a-615009fea233"), "informational", "React framework and tooling information", "React", "react-runtime" },
                    { new Guid("30e21343-9124-4bec-b247-f780a5be12df"), "behavioral", "Whether the app starts automatically when Collabhost starts", "Auto Start", "auto-start" },
                    { new Guid("6e77364e-d15c-4a02-b12d-9d8a0d623ff3"), "behavioral", "How traffic reaches the app through the reverse proxy", "Routing", "routing" },
                    { new Guid("76e18bcb-2ee9-4c9c-9d11-fb10c8f20ee0"), "behavioral", "HTTP endpoint polled to determine app health", "Health Check", "health-check" },
                    { new Guid("8460d864-4df5-423d-bb46-c351594b6667"), "behavioral", "How the app's process is discovered, started, and stopped", "Process Management", "process" },
                    { new Guid("b4fdd637-4e1f-46fb-a5fe-83bccf14a30e"), "behavioral", "Environment variables injected when the app process starts", "Environment Variables", "environment-defaults" }
                });

            migrationBuilder.InsertData(
                table: "DiscoveryStrategy",
                columns: new[] { "Id", "Description", "DisplayName", "IsActive", "Name", "Ordinal" },
                values: new object[,]
                {
                    { new Guid("506cfcc2-b806-43c3-9baf-593053d9826e"), null, "package.json", true, "package-json", 1 },
                    { new Guid("59e2a600-f739-470b-9981-cfe538af272b"), null, "Manual", true, "manual", 2 },
                    { new Guid("f3ee2904-4e01-483f-ad94-e8237953fcfc"), null, ".NET Runtime Config", true, "dotnet-runtimeconfig", 0 }
                });

            migrationBuilder.InsertData(
                table: "ProcessState",
                columns: new[] { "Id", "Description", "DisplayName", "IsActive", "Name", "Ordinal" },
                values: new object[,]
                {
                    { new Guid("0d98c241-6630-4f0f-a16e-bad8a013eb31"), null, "Stopping", true, "Stopping", 3 },
                    { new Guid("2cd5e4ed-ac0e-40eb-abad-3b56819f97a4"), null, "Running", true, "Running", 2 },
                    { new Guid("517b2fb1-e5f9-4fdd-98de-10cedec5bcc3"), null, "Restarting", true, "Restarting", 5 },
                    { new Guid("55331f23-8eae-4e56-811d-8bbdeaca03ca"), null, "Crashed", true, "Crashed", 4 },
                    { new Guid("b9057b8a-7fe7-407c-84e2-dcdc41caeee1"), null, "Starting", true, "Starting", 1 },
                    { new Guid("d413f4ed-d764-4277-ab4c-190822d22789"), null, "Stopped", true, "Stopped", 0 }
                });

            migrationBuilder.InsertData(
                table: "RestartPolicy",
                columns: new[] { "Id", "Description", "DisplayName", "IsActive", "Name", "Ordinal" },
                values: new object[,]
                {
                    { new Guid("16657ec0-d027-497e-9bbf-eb835492f80b"), null, "On Crash", true, "on-crash", 1 },
                    { new Guid("89ff36ea-1f8a-42a2-a02d-b33b3dd2918c"), null, "Always", true, "always", 2 },
                    { new Guid("efecc013-d65f-407c-af7a-ab61043e00b2"), null, "Never", true, "never", 0 }
                });

            migrationBuilder.InsertData(
                table: "ServeMode",
                columns: new[] { "Id", "Description", "DisplayName", "IsActive", "Name", "Ordinal" },
                values: new object[,]
                {
                    { new Guid("47a7a3f0-9ab5-4194-8b1a-ee1267cb844c"), null, "File Server", true, "file-server", 1 },
                    { new Guid("56d426ec-c60b-449f-a62a-f294bb893fda"), null, "Reverse Proxy", true, "reverse-proxy", 0 }
                });

            migrationBuilder.InsertData(
                table: "AppTypeCapability",
                columns: new[] { "Id", "AppTypeId", "CapabilityId", "Configuration" },
                values: new object[,]
                {
                    { new Guid("088e99f8-dd64-4d14-bed2-e0e2027ac1b4"), new Guid("49d21824-f9e6-4a44-9b12-130f8c680cb9"), new Guid("2661f347-6af3-40a6-a0c4-57fb7e4d1f72"), "{\"policy\":\"always\"}" },
                    { new Guid("097ade9a-ab92-4a0f-ac1e-51d58e1d37cc"), new Guid("d3333aba-642e-4784-a501-856a25ae6fe5"), new Guid("1f642072-7975-4fa0-8109-4d9be5ffa909"), "{\"targetFramework\":\"net10.0\",\"runtimeVersion\":\"10.0.x\",\"selfContained\":false}" },
                    { new Guid("14cb559d-7675-43bb-acdd-f1f15671c570"), new Guid("bf5105c8-6a99-414c-96b6-c74aab5471f7"), new Guid("2661f347-6af3-40a6-a0c4-57fb7e4d1f72"), "{\"policy\":\"on-crash\"}" },
                    { new Guid("254d4414-246f-44e5-82d0-0075b3f994c0"), new Guid("bf5105c8-6a99-414c-96b6-c74aab5471f7"), new Guid("0c66bd0a-d6b1-419d-83c2-9186d773cc52"), "{\"environmentVariableName\":\"PORT\",\"portFormat\":\"{port}\"}" },
                    { new Guid("3cf3ba43-bb6a-4823-ac18-2747c57c802f"), new Guid("d3333aba-642e-4784-a501-856a25ae6fe5"), new Guid("2661f347-6af3-40a6-a0c4-57fb7e4d1f72"), "{\"policy\":\"always\"}" },
                    { new Guid("408ae72c-0771-4817-a5ed-950419ee5771"), new Guid("bf5105c8-6a99-414c-96b6-c74aab5471f7"), new Guid("30e21343-9124-4bec-b247-f780a5be12df"), "{\"enabled\":false}" },
                    { new Guid("41b486b0-037f-44e8-9a96-028c490fa48c"), new Guid("d3333aba-642e-4784-a501-856a25ae6fe5"), new Guid("6e77364e-d15c-4a02-b12d-9d8a0d623ff3"), "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"reverse-proxy\"}" },
                    { new Guid("521df52f-53cd-448c-ac85-b727fd9d7168"), new Guid("56608f77-aa9d-44b7-a3cb-df7c361d8fb8"), new Guid("2661f347-6af3-40a6-a0c4-57fb7e4d1f72"), "{\"policy\":\"on-crash\"}" },
                    { new Guid("564a6534-79a7-4610-af93-27c3916c105f"), new Guid("56608f77-aa9d-44b7-a3cb-df7c361d8fb8"), new Guid("30e21343-9124-4bec-b247-f780a5be12df"), "{\"enabled\":true}" },
                    { new Guid("61e355eb-1998-41ad-bfdc-069b643173c1"), new Guid("d3333aba-642e-4784-a501-856a25ae6fe5"), new Guid("8460d864-4df5-423d-bb46-c351594b6667"), "{\"discoveryStrategy\":\"dotnet-runtimeconfig\",\"gracefulShutdown\":true,\"shutdownTimeoutSeconds\":30}" },
                    { new Guid("6d0d10bb-0764-477d-a113-b3c1f380f598"), new Guid("d3333aba-642e-4784-a501-856a25ae6fe5"), new Guid("0c66bd0a-d6b1-419d-83c2-9186d773cc52"), "{\"environmentVariableName\":\"ASPNETCORE_URLS\",\"portFormat\":\"http://localhost:{port}\"}" },
                    { new Guid("74137008-620c-46cc-ba2c-e1e1896d25c1"), new Guid("49d21824-f9e6-4a44-9b12-130f8c680cb9"), new Guid("6e77364e-d15c-4a02-b12d-9d8a0d623ff3"), "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"reverse-proxy\"}" },
                    { new Guid("74ff801f-58f8-4fc6-8faf-bdddecd4673e"), new Guid("49d21824-f9e6-4a44-9b12-130f8c680cb9"), new Guid("0c66bd0a-d6b1-419d-83c2-9186d773cc52"), "{\"environmentVariableName\":\"PORT\",\"portFormat\":\"{port}\"}" },
                    { new Guid("7eb34403-7641-49aa-8a0c-7f30a39d2355"), new Guid("d3333aba-642e-4784-a501-856a25ae6fe5"), new Guid("b4fdd637-4e1f-46fb-a5fe-83bccf14a30e"), "{\"defaults\":{\"ASPNETCORE_ENVIRONMENT\":\"Production\"}}" },
                    { new Guid("84810cdc-4299-42f1-903d-8fff82ed4e92"), new Guid("49d21824-f9e6-4a44-9b12-130f8c680cb9"), new Guid("0ba21247-bf12-4487-b2ad-e4c84a784d75"), "{\"nodeVersion\":\"22.x\",\"packageManager\":\"npm\"}" },
                    { new Guid("909f7e6d-451c-4bff-b8d5-bfb04f3b5116"), new Guid("49d21824-f9e6-4a44-9b12-130f8c680cb9"), new Guid("30e21343-9124-4bec-b247-f780a5be12df"), "{\"enabled\":true}" },
                    { new Guid("a4cbac96-a44d-4823-9924-e4a530ee96b2"), new Guid("606cdf1f-f41e-42d2-bb13-04b598de0f63"), new Guid("6e77364e-d15c-4a02-b12d-9d8a0d623ff3"), "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"file-server\",\"spaFallback\":false}" },
                    { new Guid("a5792083-b9e7-4ef7-8f3f-b9835d908362"), new Guid("49d21824-f9e6-4a44-9b12-130f8c680cb9"), new Guid("76e18bcb-2ee9-4c9c-9d11-fb10c8f20ee0"), "{\"endpoint\":\"/health\",\"intervalSeconds\":30,\"timeoutSeconds\":5,\"retries\":3}" },
                    { new Guid("b256a4b1-86fe-46db-ab50-0061e7854996"), new Guid("73e28a95-764f-4ae5-9c2b-a9fdea66c348"), new Guid("2bc51a48-5e56-4c27-958a-615009fea233"), "{\"reactVersion\":\"18.x\",\"router\":\"react-router\",\"bundler\":\"vite\"}" },
                    { new Guid("d9c3ac3e-052c-4992-ac36-bd3079499663"), new Guid("73e28a95-764f-4ae5-9c2b-a9fdea66c348"), new Guid("0ba21247-bf12-4487-b2ad-e4c84a784d75"), "{\"nodeVersion\":\"22.x\",\"packageManager\":\"npm\",\"buildCommand\":\"npm run build\"}" },
                    { new Guid("e708632b-d307-4045-9778-679d979b1578"), new Guid("73e28a95-764f-4ae5-9c2b-a9fdea66c348"), new Guid("6e77364e-d15c-4a02-b12d-9d8a0d623ff3"), "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"file-server\",\"spaFallback\":true}" },
                    { new Guid("ecb72690-91c9-43ae-9039-92ada963271c"), new Guid("bf5105c8-6a99-414c-96b6-c74aab5471f7"), new Guid("8460d864-4df5-423d-bb46-c351594b6667"), "{\"discoveryStrategy\":\"manual\",\"gracefulShutdown\":false,\"shutdownTimeoutSeconds\":10,\"command\":\"echo\",\"arguments\":\"no command configured\"}" },
                    { new Guid("eec22dfc-d996-4563-9e26-dd671f3057e2"), new Guid("56608f77-aa9d-44b7-a3cb-df7c361d8fb8"), new Guid("8460d864-4df5-423d-bb46-c351594b6667"), "{\"discoveryStrategy\":\"manual\",\"gracefulShutdown\":true,\"shutdownTimeoutSeconds\":10,\"command\":\"echo\",\"arguments\":\"no command configured\"}" },
                    { new Guid("f51fc05c-924b-4f6d-b0e0-8193b676a6f5"), new Guid("49d21824-f9e6-4a44-9b12-130f8c680cb9"), new Guid("8460d864-4df5-423d-bb46-c351594b6667"), "{\"discoveryStrategy\":\"package-json\",\"gracefulShutdown\":true,\"shutdownTimeoutSeconds\":30}" },
                    { new Guid("f82da976-2c10-428c-a192-6ebee06107ab"), new Guid("d3333aba-642e-4784-a501-856a25ae6fe5"), new Guid("76e18bcb-2ee9-4c9c-9d11-fb10c8f20ee0"), "{\"endpoint\":\"/health\",\"intervalSeconds\":30,\"timeoutSeconds\":5,\"retries\":3}" },
                    { new Guid("fa660eb9-810a-47c1-8010-799481c4dca5"), new Guid("bf5105c8-6a99-414c-96b6-c74aab5471f7"), new Guid("6e77364e-d15c-4a02-b12d-9d8a0d623ff3"), "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"reverse-proxy\"}" },
                    { new Guid("fcd84bc2-a09c-4e1b-a1e8-2a660a0d3113"), new Guid("d3333aba-642e-4784-a501-856a25ae6fe5"), new Guid("30e21343-9124-4bec-b247-f780a5be12df"), "{\"enabled\":true}" }
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
                name: "DiscoveryStrategy");

            migrationBuilder.DropTable(
                name: "ProcessState");

            migrationBuilder.DropTable(
                name: "RestartPolicy");

            migrationBuilder.DropTable(
                name: "ServeMode");

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
