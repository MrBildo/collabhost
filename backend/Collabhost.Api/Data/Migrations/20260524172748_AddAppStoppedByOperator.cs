using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Collabhost.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAppStoppedByOperator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "StoppedByOperator",
                table: "Apps",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StoppedByOperator",
                table: "Apps");
        }
    }
}
