using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Collabhost.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDotnetEnvironmentVariables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "CapabilityBindings",
                keyColumn: "Id",
                keyValue: "01KN8K1MRT0B5TF2TTXV68DAFJ",
                column: "DefaultConfigurationJson",
                value: "{\"variables\":{\"ASPNETCORE_ENVIRONMENT\":\"Production\",\"DOTNET_ENVIRONMENT\":\"Production\",\"DOTNET_NOLOGO\":\"1\"}}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "CapabilityBindings",
                keyColumn: "Id",
                keyValue: "01KN8K1MRT0B5TF2TTXV68DAFJ",
                column: "DefaultConfigurationJson",
                value: "{\"variables\":{\"ASPNETCORE_ENVIRONMENT\":\"Production\"}}");
        }
    }
}
