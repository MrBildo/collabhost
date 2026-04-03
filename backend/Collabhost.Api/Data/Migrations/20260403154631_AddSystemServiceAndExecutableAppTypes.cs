using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Collabhost.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemServiceAndExecutableAppTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AppTypes",
                keyColumn: "Id",
                keyValue: "01KN8K1MRQ0K06ADYJJ8VAXG5Y",
                column: "CreatedAt",
                value: new DateTime(2026, 4, 3, 15, 46, 31, 356, DateTimeKind.Utc).AddTicks(4311));

            migrationBuilder.UpdateData(
                table: "AppTypes",
                keyColumn: "Id",
                keyValue: "01KN8K1MRT26VCX65J1ZSVWESB",
                column: "CreatedAt",
                value: new DateTime(2026, 4, 3, 15, 46, 31, 357, DateTimeKind.Utc).AddTicks(1034));

            migrationBuilder.UpdateData(
                table: "AppTypes",
                keyColumn: "Id",
                keyValue: "01KN8K1MRT4XGXXW5BBQ8YZNN2",
                column: "CreatedAt",
                value: new DateTime(2026, 4, 3, 15, 46, 31, 357, DateTimeKind.Utc).AddTicks(1023));

            migrationBuilder.InsertData(
                table: "AppTypes",
                columns: new[] { "Id", "CreatedAt", "Description", "DisplayName", "IsBuiltIn", "MetadataJson", "Slug" },
                values: new object[,]
                {
                    { "01KNA0A0ZN42VV6T9GTEPS17CD", new DateTime(2026, 4, 3, 15, 46, 31, 357, DateTimeKind.Utc).AddTicks(1039), "Infrastructure process with no routing or port injection", "System Service", true, null, "system-service" },
                    { "01KNA0A0ZRZE6W7RPX9BRREKNQ", new DateTime(2026, 4, 3, 15, 46, 31, 357, DateTimeKind.Utc).AddTicks(1043), "Generic binary with port injection and reverse proxy routing", "Executable", true, null, "executable" }
                });

            migrationBuilder.UpdateData(
                table: "CapabilityBindings",
                keyColumn: "Id",
                keyValue: "01KN8K1MRTETFY88Z8FTJGCBB5",
                column: "DefaultConfigurationJson",
                value: "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"FileServer\",\"spaFallback\":false}");

            migrationBuilder.InsertData(
                table: "CapabilityBindings",
                columns: new[] { "Id", "AppTypeId", "CapabilitySlug", "DefaultConfigurationJson" },
                values: new object[,]
                {
                    { "01KNA0A0ZR0AAGHBHWGZND1C0J", "01KNA0A0ZRZE6W7RPX9BRREKNQ", "process", "{\"discoveryStrategy\":\"Manual\",\"gracefulShutdown\":false,\"shutdownTimeoutSeconds\":10}" },
                    { "01KNA0A0ZR1VEJ6DCGFGS4M97Q", "01KNA0A0ZRZE6W7RPX9BRREKNQ", "routing", "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"ReverseProxy\",\"spaFallback\":false}" },
                    { "01KNA0A0ZR2V1V5VMMR6TR8BPC", "01KNA0A0ZRZE6W7RPX9BRREKNQ", "auto-start", "{\"enabled\":false}" },
                    { "01KNA0A0ZRD5350M0C0Y8AZ62V", "01KNA0A0ZRZE6W7RPX9BRREKNQ", "artifact", "{\"location\":\"\"}" },
                    { "01KNA0A0ZRDCSEHPT4E069WFQS", "01KNA0A0ZN42VV6T9GTEPS17CD", "process", "{\"discoveryStrategy\":\"Manual\",\"gracefulShutdown\":true,\"shutdownTimeoutSeconds\":10}" },
                    { "01KNA0A0ZRFWXKV2HS4TS5ACA7", "01KNA0A0ZN42VV6T9GTEPS17CD", "restart", "{\"policy\":\"OnCrash\"}" },
                    { "01KNA0A0ZRKQQ4TJK7E2Z3RX4J", "01KNA0A0ZN42VV6T9GTEPS17CD", "artifact", "{\"location\":\"\"}" },
                    { "01KNA0A0ZRVMFJBHFQ623KG0SM", "01KNA0A0ZRZE6W7RPX9BRREKNQ", "port-injection", "{\"environmentVariableName\":\"PORT\",\"portFormat\":\"{port}\"}" },
                    { "01KNA0A0ZRY7GQTTCX8HY6S97W", "01KNA0A0ZRZE6W7RPX9BRREKNQ", "restart", "{\"policy\":\"OnCrash\"}" },
                    { "01KNA0A0ZRYZN3ZPXS6ENHHHH5", "01KNA0A0ZN42VV6T9GTEPS17CD", "auto-start", "{\"enabled\":true}" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "CapabilityBindings",
                keyColumn: "Id",
                keyValue: "01KNA0A0ZR0AAGHBHWGZND1C0J");

            migrationBuilder.DeleteData(
                table: "CapabilityBindings",
                keyColumn: "Id",
                keyValue: "01KNA0A0ZR1VEJ6DCGFGS4M97Q");

            migrationBuilder.DeleteData(
                table: "CapabilityBindings",
                keyColumn: "Id",
                keyValue: "01KNA0A0ZR2V1V5VMMR6TR8BPC");

            migrationBuilder.DeleteData(
                table: "CapabilityBindings",
                keyColumn: "Id",
                keyValue: "01KNA0A0ZRD5350M0C0Y8AZ62V");

            migrationBuilder.DeleteData(
                table: "CapabilityBindings",
                keyColumn: "Id",
                keyValue: "01KNA0A0ZRDCSEHPT4E069WFQS");

            migrationBuilder.DeleteData(
                table: "CapabilityBindings",
                keyColumn: "Id",
                keyValue: "01KNA0A0ZRFWXKV2HS4TS5ACA7");

            migrationBuilder.DeleteData(
                table: "CapabilityBindings",
                keyColumn: "Id",
                keyValue: "01KNA0A0ZRKQQ4TJK7E2Z3RX4J");

            migrationBuilder.DeleteData(
                table: "CapabilityBindings",
                keyColumn: "Id",
                keyValue: "01KNA0A0ZRVMFJBHFQ623KG0SM");

            migrationBuilder.DeleteData(
                table: "CapabilityBindings",
                keyColumn: "Id",
                keyValue: "01KNA0A0ZRY7GQTTCX8HY6S97W");

            migrationBuilder.DeleteData(
                table: "CapabilityBindings",
                keyColumn: "Id",
                keyValue: "01KNA0A0ZRYZN3ZPXS6ENHHHH5");

            migrationBuilder.DeleteData(
                table: "AppTypes",
                keyColumn: "Id",
                keyValue: "01KNA0A0ZN42VV6T9GTEPS17CD");

            migrationBuilder.DeleteData(
                table: "AppTypes",
                keyColumn: "Id",
                keyValue: "01KNA0A0ZRZE6W7RPX9BRREKNQ");

            migrationBuilder.UpdateData(
                table: "AppTypes",
                keyColumn: "Id",
                keyValue: "01KN8K1MRQ0K06ADYJJ8VAXG5Y",
                column: "CreatedAt",
                value: new DateTime(2026, 4, 3, 4, 37, 12, 686, DateTimeKind.Utc).AddTicks(5749));

            migrationBuilder.UpdateData(
                table: "AppTypes",
                keyColumn: "Id",
                keyValue: "01KN8K1MRT26VCX65J1ZSVWESB",
                column: "CreatedAt",
                value: new DateTime(2026, 4, 3, 4, 37, 12, 687, DateTimeKind.Utc).AddTicks(2122));

            migrationBuilder.UpdateData(
                table: "AppTypes",
                keyColumn: "Id",
                keyValue: "01KN8K1MRT4XGXXW5BBQ8YZNN2",
                column: "CreatedAt",
                value: new DateTime(2026, 4, 3, 4, 37, 12, 687, DateTimeKind.Utc).AddTicks(2117));

            migrationBuilder.UpdateData(
                table: "CapabilityBindings",
                keyColumn: "Id",
                keyValue: "01KN8K1MRTETFY88Z8FTJGCBB5",
                column: "DefaultConfigurationJson",
                value: "{\"domainPattern\":\"{slug}.collab.internal\",\"serveMode\":\"FileServer\",\"spaFallback\":true}");
        }
    }
}
