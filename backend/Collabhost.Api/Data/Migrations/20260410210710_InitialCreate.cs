using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Collabhost.Api.Data.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ActivityEvents",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                EventType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                ActorId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                ActorName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                AppId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                AppSlug = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ActivityEvents", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Apps",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                Slug = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                AppTypeSlug = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                RegisteredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Apps", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Users",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                AuthKey = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                Role = table.Column<int>(type: "INTEGER", nullable: false),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Users", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "CapabilityOverrides",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                AppId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                CapabilitySlug = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                ConfigurationJson = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CapabilityOverrides", x => x.Id);
                table.ForeignKey(
                    name: "FK_CapabilityOverrides_Apps_AppId",
                    column: x => x.AppId,
                    principalTable: "Apps",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ActivityEvents_ActorId",
            table: "ActivityEvents",
            column: "ActorId");

        migrationBuilder.CreateIndex(
            name: "IX_ActivityEvents_AppSlug",
            table: "ActivityEvents",
            column: "AppSlug");

        migrationBuilder.CreateIndex(
            name: "IX_ActivityEvents_EventType",
            table: "ActivityEvents",
            column: "EventType");

        migrationBuilder.CreateIndex(
            name: "IX_ActivityEvents_Timestamp",
            table: "ActivityEvents",
            column: "Timestamp");

        migrationBuilder.CreateIndex(
            name: "IX_Apps_Slug",
            table: "Apps",
            column: "Slug",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CapabilityOverrides_AppId_CapabilitySlug",
            table: "CapabilityOverrides",
            columns: ["AppId", "CapabilitySlug"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Users_AuthKey",
            table: "Users",
            column: "AuthKey",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ActivityEvents");

        migrationBuilder.DropTable(
            name: "CapabilityOverrides");

        migrationBuilder.DropTable(
            name: "Users");

        migrationBuilder.DropTable(
            name: "Apps");
    }
}
