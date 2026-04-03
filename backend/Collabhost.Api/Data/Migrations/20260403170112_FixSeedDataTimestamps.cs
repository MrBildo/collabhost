using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Collabhost.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixSeedDataTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AppTypes",
                keyColumn: "Id",
                keyValue: "01KN8K1MRQ0K06ADYJJ8VAXG5Y",
                column: "CreatedAt",
                value: new DateTime(2026, 4, 3, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "AppTypes",
                keyColumn: "Id",
                keyValue: "01KN8K1MRT26VCX65J1ZSVWESB",
                column: "CreatedAt",
                value: new DateTime(2026, 4, 3, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "AppTypes",
                keyColumn: "Id",
                keyValue: "01KN8K1MRT4XGXXW5BBQ8YZNN2",
                column: "CreatedAt",
                value: new DateTime(2026, 4, 3, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "AppTypes",
                keyColumn: "Id",
                keyValue: "01KNA0A0ZN42VV6T9GTEPS17CD",
                column: "CreatedAt",
                value: new DateTime(2026, 4, 3, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "AppTypes",
                keyColumn: "Id",
                keyValue: "01KNA0A0ZRZE6W7RPX9BRREKNQ",
                column: "CreatedAt",
                value: new DateTime(2026, 4, 3, 0, 0, 0, 0, DateTimeKind.Utc));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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

            migrationBuilder.UpdateData(
                table: "AppTypes",
                keyColumn: "Id",
                keyValue: "01KNA0A0ZN42VV6T9GTEPS17CD",
                column: "CreatedAt",
                value: new DateTime(2026, 4, 3, 15, 46, 31, 357, DateTimeKind.Utc).AddTicks(1039));

            migrationBuilder.UpdateData(
                table: "AppTypes",
                keyColumn: "Id",
                keyValue: "01KNA0A0ZRZE6W7RPX9BRREKNQ",
                column: "CreatedAt",
                value: new DateTime(2026, 4, 3, 15, 46, 31, 357, DateTimeKind.Utc).AddTicks(1043));
        }
    }
}
