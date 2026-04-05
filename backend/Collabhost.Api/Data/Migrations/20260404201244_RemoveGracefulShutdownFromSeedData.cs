using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Collabhost.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveGracefulShutdownFromSeedData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "CapabilityBindings",
                keyColumn: "Id",
                keyValue: "01KN8K1MRT9RES6FCWSNFYNXGK",
                column: "DefaultConfigurationJson",
                value: "{\"discoveryStrategy\":\"PackageJson\",\"shutdownTimeoutSeconds\":15}");

            migrationBuilder.UpdateData(
                table: "CapabilityBindings",
                keyColumn: "Id",
                keyValue: "01KN8K1MRTJD1NCG0J9R4364MJ",
                column: "DefaultConfigurationJson",
                value: "{\"discoveryStrategy\":\"DotNetRuntimeConfiguration\",\"shutdownTimeoutSeconds\":30}");

            migrationBuilder.UpdateData(
                table: "CapabilityBindings",
                keyColumn: "Id",
                keyValue: "01KNA0A0ZR0AAGHBHWGZND1C0J",
                column: "DefaultConfigurationJson",
                value: "{\"discoveryStrategy\":\"Manual\",\"shutdownTimeoutSeconds\":10}");

            migrationBuilder.UpdateData(
                table: "CapabilityBindings",
                keyColumn: "Id",
                keyValue: "01KNA0A0ZRDCSEHPT4E069WFQS",
                column: "DefaultConfigurationJson",
                value: "{\"discoveryStrategy\":\"Manual\",\"shutdownTimeoutSeconds\":10}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "CapabilityBindings",
                keyColumn: "Id",
                keyValue: "01KN8K1MRT9RES6FCWSNFYNXGK",
                column: "DefaultConfigurationJson",
                value: "{\"discoveryStrategy\":\"PackageJson\",\"gracefulShutdown\":true,\"shutdownTimeoutSeconds\":15}");

            migrationBuilder.UpdateData(
                table: "CapabilityBindings",
                keyColumn: "Id",
                keyValue: "01KN8K1MRTJD1NCG0J9R4364MJ",
                column: "DefaultConfigurationJson",
                value: "{\"discoveryStrategy\":\"DotNetRuntimeConfiguration\",\"gracefulShutdown\":true,\"shutdownTimeoutSeconds\":30}");

            migrationBuilder.UpdateData(
                table: "CapabilityBindings",
                keyColumn: "Id",
                keyValue: "01KNA0A0ZR0AAGHBHWGZND1C0J",
                column: "DefaultConfigurationJson",
                value: "{\"discoveryStrategy\":\"Manual\",\"gracefulShutdown\":false,\"shutdownTimeoutSeconds\":10}");

            migrationBuilder.UpdateData(
                table: "CapabilityBindings",
                keyColumn: "Id",
                keyValue: "01KNA0A0ZRDCSEHPT4E069WFQS",
                column: "DefaultConfigurationJson",
                value: "{\"discoveryStrategy\":\"Manual\",\"gracefulShutdown\":true,\"shutdownTimeoutSeconds\":10}");
        }
    }
}
