using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Collabhost.Api.Migrations;

/// <inheritdoc />
public partial class AddUpdateTimeoutSeconds : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<int>(
            name: "UpdateTimeoutSeconds",
            table: "App",
            type: "INTEGER",
            nullable: true);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
            name: "UpdateTimeoutSeconds",
            table: "App");
}
