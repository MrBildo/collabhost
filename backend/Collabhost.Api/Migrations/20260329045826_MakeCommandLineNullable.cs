using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Collabhost.Api.Migrations;

/// <inheritdoc />
public partial class MakeCommandLineNullable : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "CommandLine",
            table: "App",
            type: "TEXT",
            maxLength: 500,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 500);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "CommandLine",
            table: "App",
            type: "TEXT",
            maxLength: 500,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 500,
            oldNullable: true);
    }
}
