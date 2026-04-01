using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional
#pragma warning disable IDE0300 // Collection initialization can be simplified — auto-generated migration

namespace Collabhost.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLookupEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.UpdateData(
                table: "AppType",
                keyColumn: "Id",
                keyValue: new Guid("49d21824-f9e6-4a44-9b12-130f8c680cb9"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AppType",
                keyColumn: "Id",
                keyValue: new Guid("606cdf1f-f41e-42d2-bb13-04b598de0f63"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AppType",
                keyColumn: "Id",
                keyValue: new Guid("73e28a95-764f-4ae5-9c2b-a9fdea66c348"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AppType",
                keyColumn: "Id",
                keyValue: new Guid("bf5105c8-6a99-414c-96b6-c74aab5471f7"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AppType",
                keyColumn: "Id",
                keyValue: new Guid("d3333aba-642e-4784-a501-856a25ae6fe5"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "AppType",
                columns: new[] { "Id", "Description", "DisplayName", "ExternalId", "IsBuiltIn", "Name" },
                values: new object[] { new Guid("a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d"), "System process with management but no routing", "System Service", "01KN0P7JYN5QSVC3SYSTEM0SVC", true, "system-service" });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("088e99f8-dd64-4d14-bed2-e0e2027ac1b4"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("097ade9a-ab92-4a0f-ac1e-51d58e1d37cc"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("14cb559d-7675-43bb-acdd-f1f15671c570"),
                columns: new[] { "Configuration", "CreatedAt", "UpdatedAt" },
                values: new object[] { "{\"policy\":\"on-crash\"}", new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("254d4414-246f-44e5-82d0-0075b3f994c0"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("3cf3ba43-bb6a-4823-ac18-2747c57c802f"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("408ae72c-0771-4817-a5ed-950419ee5771"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("41b486b0-037f-44e8-9a96-028c490fa48c"),
                columns: new[] { "Configuration", "CreatedAt", "UpdatedAt" },
                values: new object[] { "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"reverse-proxy\"}", new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("61e355eb-1998-41ad-bfdc-069b643173c1"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("6d0d10bb-0764-477d-a113-b3c1f380f598"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("74137008-620c-46cc-ba2c-e1e1896d25c1"),
                columns: new[] { "Configuration", "CreatedAt", "UpdatedAt" },
                values: new object[] { "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"reverse-proxy\"}", new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("74ff801f-58f8-4fc6-8faf-bdddecd4673e"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("7eb34403-7641-49aa-8a0c-7f30a39d2355"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("84810cdc-4299-42f1-903d-8fff82ed4e92"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("909f7e6d-451c-4bff-b8d5-bfb04f3b5116"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("a4cbac96-a44d-4823-9924-e4a530ee96b2"),
                columns: new[] { "Configuration", "CreatedAt", "UpdatedAt" },
                values: new object[] { "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"file-server\",\"spaFallback\":false}", new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("a5792083-b9e7-4ef7-8f3f-b9835d908362"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("b256a4b1-86fe-46db-ab50-0061e7854996"),
                columns: new[] { "Configuration", "CreatedAt", "UpdatedAt" },
                values: new object[] { "{\"reactVersion\":\"18.x\",\"router\":\"react-router\",\"bundler\":\"vite\"}", new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("d9c3ac3e-052c-4992-ac36-bd3079499663"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("e708632b-d307-4045-9778-679d979b1578"),
                columns: new[] { "Configuration", "CreatedAt", "UpdatedAt" },
                values: new object[] { "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"file-server\",\"spaFallback\":true}", new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("ecb72690-91c9-43ae-9039-92ada963271c"),
                columns: new[] { "Configuration", "CreatedAt", "UpdatedAt" },
                values: new object[] { "{\"discoveryStrategy\":\"manual\",\"gracefulShutdown\":false,\"shutdownTimeoutSeconds\":10,\"command\":\"echo\",\"arguments\":\"no command configured\"}", new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("f51fc05c-924b-4f6d-b0e0-8193b676a6f5"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("f82da976-2c10-428c-a192-6ebee06107ab"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("fa660eb9-810a-47c1-8010-799481c4dca5"),
                columns: new[] { "Configuration", "CreatedAt", "UpdatedAt" },
                values: new object[] { "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"reverse-proxy\"}", new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("fcd84bc2-a09c-4e1b-a1e8-2a660a0d3113"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("0ba21247-bf12-4487-b2ad-e4c84a784d75"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("0c66bd0a-d6b1-419d-83c2-9186d773cc52"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("1f642072-7975-4fa0-8109-4d9be5ffa909"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("2661f347-6af3-40a6-a0c4-57fb7e4d1f72"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("2bc51a48-5e56-4c27-958a-615009fea233"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("30e21343-9124-4bec-b247-f780a5be12df"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("6e77364e-d15c-4a02-b12d-9d8a0d623ff3"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("76e18bcb-2ee9-4c9c-9d11-fb10c8f20ee0"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("8460d864-4df5-423d-bb46-c351594b6667"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("b4fdd637-4e1f-46fb-a5fe-83bccf14a30e"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "DiscoveryStrategy",
                columns: new[] { "Id", "Description", "DisplayName", "IsActive", "Name", "Ordinal" },
                values: new object[,]
                {
                    { new Guid("c3456789-0003-4000-8000-000000000001"), null, ".NET Runtime Config", true, "dotnet-runtimeconfig", 0 },
                    { new Guid("c3456789-0003-4000-8000-000000000002"), null, "package.json", true, "package-json", 1 },
                    { new Guid("c3456789-0003-4000-8000-000000000003"), null, "Manual", true, "manual", 2 }
                });

            migrationBuilder.UpdateData(
                table: "ProcessState",
                keyColumn: "Id",
                keyValue: new Guid("0d98c241-6630-4f0f-a16e-bad8a013eb31"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "ProcessState",
                keyColumn: "Id",
                keyValue: new Guid("2cd5e4ed-ac0e-40eb-abad-3b56819f97a4"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "ProcessState",
                keyColumn: "Id",
                keyValue: new Guid("517b2fb1-e5f9-4fdd-98de-10cedec5bcc3"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "ProcessState",
                keyColumn: "Id",
                keyValue: new Guid("55331f23-8eae-4e56-811d-8bbdeaca03ca"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "ProcessState",
                keyColumn: "Id",
                keyValue: new Guid("b9057b8a-7fe7-407c-84e2-dcdc41caeee1"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "ProcessState",
                keyColumn: "Id",
                keyValue: new Guid("d413f4ed-d764-4277-ab4c-190822d22789"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "RestartPolicy",
                columns: new[] { "Id", "Description", "DisplayName", "IsActive", "Name", "Ordinal" },
                values: new object[,]
                {
                    { new Guid("a1234567-0001-4000-8000-000000000001"), null, "Never", true, "never", 0 },
                    { new Guid("a1234567-0001-4000-8000-000000000002"), null, "On Crash", true, "on-crash", 1 },
                    { new Guid("a1234567-0001-4000-8000-000000000003"), null, "Always", true, "always", 2 }
                });

            migrationBuilder.InsertData(
                table: "ServeMode",
                columns: new[] { "Id", "Description", "DisplayName", "IsActive", "Name", "Ordinal" },
                values: new object[,]
                {
                    { new Guid("b2345678-0002-4000-8000-000000000001"), null, "Reverse Proxy", true, "reverse-proxy", 0 },
                    { new Guid("b2345678-0002-4000-8000-000000000002"), null, "File Server", true, "file-server", 1 }
                });

            migrationBuilder.InsertData(
                table: "AppTypeCapability",
                columns: new[] { "Id", "AppTypeId", "CapabilityId", "Configuration" },
                values: new object[,]
                {
                    { new Guid("c1d2e3f4-a5b6-4c7d-8e9f-0a1b2c3d4e5f"), new Guid("a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d"), new Guid("8460d864-4df5-423d-bb46-c351594b6667"), "{\"discoveryStrategy\":\"manual\",\"gracefulShutdown\":true,\"shutdownTimeoutSeconds\":10,\"command\":\"echo\",\"arguments\":\"no command configured\"}" },
                    { new Guid("d2e3f4a5-b6c7-4d8e-9f0a-1b2c3d4e5f6a"), new Guid("a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d"), new Guid("2661f347-6af3-40a6-a0c4-57fb7e4d1f72"), "{\"policy\":\"on-crash\"}" },
                    { new Guid("e3f4a5b6-c7d8-4e9f-0a1b-2c3d4e5f6a7b"), new Guid("a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d"), new Guid("30e21343-9124-4bec-b247-f780a5be12df"), "{\"enabled\":true}" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiscoveryStrategy");

            migrationBuilder.DropTable(
                name: "RestartPolicy");

            migrationBuilder.DropTable(
                name: "ServeMode");

            migrationBuilder.DeleteData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("c1d2e3f4-a5b6-4c7d-8e9f-0a1b2c3d4e5f"));

            migrationBuilder.DeleteData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("d2e3f4a5-b6c7-4d8e-9f0a-1b2c3d4e5f6a"));

            migrationBuilder.DeleteData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("e3f4a5b6-c7d8-4e9f-0a1b-2c3d4e5f6a7b"));

            migrationBuilder.DeleteData(
                table: "AppType",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d"));

            migrationBuilder.UpdateData(
                table: "AppType",
                keyColumn: "Id",
                keyValue: new Guid("49d21824-f9e6-4a44-9b12-130f8c680cb9"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppType",
                keyColumn: "Id",
                keyValue: new Guid("606cdf1f-f41e-42d2-bb13-04b598de0f63"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppType",
                keyColumn: "Id",
                keyValue: new Guid("73e28a95-764f-4ae5-9c2b-a9fdea66c348"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppType",
                keyColumn: "Id",
                keyValue: new Guid("bf5105c8-6a99-414c-96b6-c74aab5471f7"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppType",
                keyColumn: "Id",
                keyValue: new Guid("d3333aba-642e-4784-a501-856a25ae6fe5"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("088e99f8-dd64-4d14-bed2-e0e2027ac1b4"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("097ade9a-ab92-4a0f-ac1e-51d58e1d37cc"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("14cb559d-7675-43bb-acdd-f1f15671c570"),
                columns: new[] { "Configuration", "CreatedAt", "UpdatedAt" },
                values: new object[] { "{\"policy\":\"onCrash\"}", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("254d4414-246f-44e5-82d0-0075b3f994c0"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("3cf3ba43-bb6a-4823-ac18-2747c57c802f"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("408ae72c-0771-4817-a5ed-950419ee5771"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("41b486b0-037f-44e8-9a96-028c490fa48c"),
                columns: new[] { "Configuration", "CreatedAt", "UpdatedAt" },
                values: new object[] { "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"reverseProxy\"}", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("61e355eb-1998-41ad-bfdc-069b643173c1"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("6d0d10bb-0764-477d-a113-b3c1f380f598"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("74137008-620c-46cc-ba2c-e1e1896d25c1"),
                columns: new[] { "Configuration", "CreatedAt", "UpdatedAt" },
                values: new object[] { "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"reverseProxy\"}", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("74ff801f-58f8-4fc6-8faf-bdddecd4673e"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("7eb34403-7641-49aa-8a0c-7f30a39d2355"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("84810cdc-4299-42f1-903d-8fff82ed4e92"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("909f7e6d-451c-4bff-b8d5-bfb04f3b5116"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("a4cbac96-a44d-4823-9924-e4a530ee96b2"),
                columns: new[] { "Configuration", "CreatedAt", "UpdatedAt" },
                values: new object[] { "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"fileServer\",\"spaFallback\":false}", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("a5792083-b9e7-4ef7-8f3f-b9835d908362"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("b256a4b1-86fe-46db-ab50-0061e7854996"),
                columns: new[] { "Configuration", "CreatedAt", "UpdatedAt" },
                values: new object[] { "{\"version\":\"18.x\",\"router\":\"react-router\",\"bundler\":\"vite\"}", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("d9c3ac3e-052c-4992-ac36-bd3079499663"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("e708632b-d307-4045-9778-679d979b1578"),
                columns: new[] { "Configuration", "CreatedAt", "UpdatedAt" },
                values: new object[] { "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"fileServer\",\"spaFallback\":true}", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("ecb72690-91c9-43ae-9039-92ada963271c"),
                columns: new[] { "Configuration", "CreatedAt", "UpdatedAt" },
                values: new object[] { "{\"discoveryStrategy\":\"manual\",\"gracefulShutdown\":false,\"shutdownTimeoutSeconds\":10}", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("f51fc05c-924b-4f6d-b0e0-8193b676a6f5"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("f82da976-2c10-428c-a192-6ebee06107ab"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("fa660eb9-810a-47c1-8010-799481c4dca5"),
                columns: new[] { "Configuration", "CreatedAt", "UpdatedAt" },
                values: new object[] { "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"reverseProxy\"}", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("fcd84bc2-a09c-4e1b-a1e8-2a660a0d3113"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("0ba21247-bf12-4487-b2ad-e4c84a784d75"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("0c66bd0a-d6b1-419d-83c2-9186d773cc52"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("1f642072-7975-4fa0-8109-4d9be5ffa909"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("2661f347-6af3-40a6-a0c4-57fb7e4d1f72"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("2bc51a48-5e56-4c27-958a-615009fea233"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("30e21343-9124-4bec-b247-f780a5be12df"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("6e77364e-d15c-4a02-b12d-9d8a0d623ff3"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("76e18bcb-2ee9-4c9c-9d11-fb10c8f20ee0"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("8460d864-4df5-423d-bb46-c351594b6667"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("b4fdd637-4e1f-46fb-a5fe-83bccf14a30e"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "ProcessState",
                keyColumn: "Id",
                keyValue: new Guid("0d98c241-6630-4f0f-a16e-bad8a013eb31"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "ProcessState",
                keyColumn: "Id",
                keyValue: new Guid("2cd5e4ed-ac0e-40eb-abad-3b56819f97a4"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "ProcessState",
                keyColumn: "Id",
                keyValue: new Guid("517b2fb1-e5f9-4fdd-98de-10cedec5bcc3"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "ProcessState",
                keyColumn: "Id",
                keyValue: new Guid("55331f23-8eae-4e56-811d-8bbdeaca03ca"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "ProcessState",
                keyColumn: "Id",
                keyValue: new Guid("b9057b8a-7fe7-407c-84e2-dcdc41caeee1"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });

            migrationBuilder.UpdateData(
                table: "ProcessState",
                keyColumn: "Id",
                keyValue: new Guid("d413f4ed-d764-4277-ab4c-190822d22789"),
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified) });
        }
    }
}
