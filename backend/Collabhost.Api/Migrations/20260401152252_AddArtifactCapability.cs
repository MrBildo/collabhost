using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Collabhost.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddArtifactCapability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AppType",
                keyColumn: "Id",
                keyValue: new Guid("49d21824-f9e6-4a44-9b12-130f8c680cb9"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppType",
                keyColumn: "Id",
                keyValue: new Guid("56608f77-aa9d-44b7-a3cb-df7c361d8fb8"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppType",
                keyColumn: "Id",
                keyValue: new Guid("606cdf1f-f41e-42d2-bb13-04b598de0f63"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppType",
                keyColumn: "Id",
                keyValue: new Guid("73e28a95-764f-4ae5-9c2b-a9fdea66c348"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppType",
                keyColumn: "Id",
                keyValue: new Guid("bf5105c8-6a99-414c-96b6-c74aab5471f7"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppType",
                keyColumn: "Id",
                keyValue: new Guid("d3333aba-642e-4784-a501-856a25ae6fe5"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("088e99f8-dd64-4d14-bed2-e0e2027ac1b4"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("097ade9a-ab92-4a0f-ac1e-51d58e1d37cc"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("14cb559d-7675-43bb-acdd-f1f15671c570"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("254d4414-246f-44e5-82d0-0075b3f994c0"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("3cf3ba43-bb6a-4823-ac18-2747c57c802f"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("408ae72c-0771-4817-a5ed-950419ee5771"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("41b486b0-037f-44e8-9a96-028c490fa48c"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("521df52f-53cd-448c-ac85-b727fd9d7168"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("564a6534-79a7-4610-af93-27c3916c105f"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("61e355eb-1998-41ad-bfdc-069b643173c1"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("6d0d10bb-0764-477d-a113-b3c1f380f598"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("74137008-620c-46cc-ba2c-e1e1896d25c1"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("74ff801f-58f8-4fc6-8faf-bdddecd4673e"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("7eb34403-7641-49aa-8a0c-7f30a39d2355"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("84810cdc-4299-42f1-903d-8fff82ed4e92"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("909f7e6d-451c-4bff-b8d5-bfb04f3b5116"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("a4cbac96-a44d-4823-9924-e4a530ee96b2"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("a5792083-b9e7-4ef7-8f3f-b9835d908362"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("b256a4b1-86fe-46db-ab50-0061e7854996"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("d9c3ac3e-052c-4992-ac36-bd3079499663"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("e708632b-d307-4045-9778-679d979b1578"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("ecb72690-91c9-43ae-9039-92ada963271c"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("eec22dfc-d996-4563-9e26-dd671f3057e2"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("f51fc05c-924b-4f6d-b0e0-8193b676a6f5"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("f82da976-2c10-428c-a192-6ebee06107ab"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("fa660eb9-810a-47c1-8010-799481c4dca5"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("fcd84bc2-a09c-4e1b-a1e8-2a660a0d3113"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("0ba21247-bf12-4487-b2ad-e4c84a784d75"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("0c66bd0a-d6b1-419d-83c2-9186d773cc52"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("1f642072-7975-4fa0-8109-4d9be5ffa909"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("2661f347-6af3-40a6-a0c4-57fb7e4d1f72"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("2bc51a48-5e56-4c27-958a-615009fea233"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("30e21343-9124-4bec-b247-f780a5be12df"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("6e77364e-d15c-4a02-b12d-9d8a0d623ff3"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("76e18bcb-2ee9-4c9c-9d11-fb10c8f20ee0"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("8460d864-4df5-423d-bb46-c351594b6667"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("b4fdd637-4e1f-46fb-a5fe-83bccf14a30e"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.InsertData(
                table: "Capability",
                columns: ["Id", "Category", "Description", "DisplayName", "Slug"],
                values: [new Guid("cdd957fc-0402-45fe-9a37-788545a2ea91"), "behavioral", "Where the app's files are located on the host filesystem", "Artifact", "artifact"]);

            migrationBuilder.UpdateData(
                table: "DiscoveryStrategy",
                keyColumn: "Id",
                keyValue: new Guid("506cfcc2-b806-43c3-9baf-593053d9826e"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "DiscoveryStrategy",
                keyColumn: "Id",
                keyValue: new Guid("59e2a600-f739-470b-9981-cfe538af272b"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "DiscoveryStrategy",
                keyColumn: "Id",
                keyValue: new Guid("f3ee2904-4e01-483f-ad94-e8237953fcfc"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "ProcessState",
                keyColumn: "Id",
                keyValue: new Guid("0d98c241-6630-4f0f-a16e-bad8a013eb31"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "ProcessState",
                keyColumn: "Id",
                keyValue: new Guid("2cd5e4ed-ac0e-40eb-abad-3b56819f97a4"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "ProcessState",
                keyColumn: "Id",
                keyValue: new Guid("517b2fb1-e5f9-4fdd-98de-10cedec5bcc3"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "ProcessState",
                keyColumn: "Id",
                keyValue: new Guid("55331f23-8eae-4e56-811d-8bbdeaca03ca"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "ProcessState",
                keyColumn: "Id",
                keyValue: new Guid("b9057b8a-7fe7-407c-84e2-dcdc41caeee1"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "ProcessState",
                keyColumn: "Id",
                keyValue: new Guid("d413f4ed-d764-4277-ab4c-190822d22789"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "RestartPolicy",
                keyColumn: "Id",
                keyValue: new Guid("16657ec0-d027-497e-9bbf-eb835492f80b"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "RestartPolicy",
                keyColumn: "Id",
                keyValue: new Guid("89ff36ea-1f8a-42a2-a02d-b33b3dd2918c"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "RestartPolicy",
                keyColumn: "Id",
                keyValue: new Guid("efecc013-d65f-407c-af7a-ab61043e00b2"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "ServeMode",
                keyColumn: "Id",
                keyValue: new Guid("47a7a3f0-9ab5-4194-8b1a-ee1267cb844c"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.UpdateData(
                table: "ServeMode",
                keyColumn: "Id",
                keyValue: new Guid("56d426ec-c60b-449f-a62a-f294bb893fda"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(1, 1, 1, 5, 0, 0, 0, DateTimeKind.Utc)]);

            migrationBuilder.InsertData(
                table: "AppTypeCapability",
                columns: ["Id", "AppTypeId", "CapabilityId", "Configuration"],
                values: new object[,]
                {
                    { new Guid("1884ba18-d094-4381-87eb-38d1b1209c1b"), new Guid("bf5105c8-6a99-414c-96b6-c74aab5471f7"), new Guid("cdd957fc-0402-45fe-9a37-788545a2ea91"), "{\"location\":\"\"}" },
                    { new Guid("21565387-1774-4a67-acac-906b7f88c0ca"), new Guid("49d21824-f9e6-4a44-9b12-130f8c680cb9"), new Guid("cdd957fc-0402-45fe-9a37-788545a2ea91"), "{\"location\":\"\"}" },
                    { new Guid("57b55f26-c099-4b2f-ba7b-04678331417f"), new Guid("d3333aba-642e-4784-a501-856a25ae6fe5"), new Guid("cdd957fc-0402-45fe-9a37-788545a2ea91"), "{\"location\":\"\"}" },
                    { new Guid("a040791b-319e-46d6-8c58-55f5ace69827"), new Guid("606cdf1f-f41e-42d2-bb13-04b598de0f63"), new Guid("cdd957fc-0402-45fe-9a37-788545a2ea91"), "{\"location\":\"\"}" },
                    { new Guid("b280d836-1434-4906-9775-3afa718b52ef"), new Guid("56608f77-aa9d-44b7-a3cb-df7c361d8fb8"), new Guid("cdd957fc-0402-45fe-9a37-788545a2ea91"), "{\"location\":\"\"}" },
                    { new Guid("ce8aba78-b73a-4e29-bae4-cc2bfd5a4cde"), new Guid("73e28a95-764f-4ae5-9c2b-a9fdea66c348"), new Guid("cdd957fc-0402-45fe-9a37-788545a2ea91"), "{\"location\":\"\"}" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("1884ba18-d094-4381-87eb-38d1b1209c1b"));

            migrationBuilder.DeleteData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("21565387-1774-4a67-acac-906b7f88c0ca"));

            migrationBuilder.DeleteData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("57b55f26-c099-4b2f-ba7b-04678331417f"));

            migrationBuilder.DeleteData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("a040791b-319e-46d6-8c58-55f5ace69827"));

            migrationBuilder.DeleteData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("b280d836-1434-4906-9775-3afa718b52ef"));

            migrationBuilder.DeleteData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("ce8aba78-b73a-4e29-bae4-cc2bfd5a4cde"));

            migrationBuilder.DeleteData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("cdd957fc-0402-45fe-9a37-788545a2ea91"));

            migrationBuilder.UpdateData(
                table: "AppType",
                keyColumn: "Id",
                keyValue: new Guid("49d21824-f9e6-4a44-9b12-130f8c680cb9"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppType",
                keyColumn: "Id",
                keyValue: new Guid("56608f77-aa9d-44b7-a3cb-df7c361d8fb8"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppType",
                keyColumn: "Id",
                keyValue: new Guid("606cdf1f-f41e-42d2-bb13-04b598de0f63"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppType",
                keyColumn: "Id",
                keyValue: new Guid("73e28a95-764f-4ae5-9c2b-a9fdea66c348"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppType",
                keyColumn: "Id",
                keyValue: new Guid("bf5105c8-6a99-414c-96b6-c74aab5471f7"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppType",
                keyColumn: "Id",
                keyValue: new Guid("d3333aba-642e-4784-a501-856a25ae6fe5"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("088e99f8-dd64-4d14-bed2-e0e2027ac1b4"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("097ade9a-ab92-4a0f-ac1e-51d58e1d37cc"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("14cb559d-7675-43bb-acdd-f1f15671c570"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("254d4414-246f-44e5-82d0-0075b3f994c0"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("3cf3ba43-bb6a-4823-ac18-2747c57c802f"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("408ae72c-0771-4817-a5ed-950419ee5771"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("41b486b0-037f-44e8-9a96-028c490fa48c"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("521df52f-53cd-448c-ac85-b727fd9d7168"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("564a6534-79a7-4610-af93-27c3916c105f"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("61e355eb-1998-41ad-bfdc-069b643173c1"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("6d0d10bb-0764-477d-a113-b3c1f380f598"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("74137008-620c-46cc-ba2c-e1e1896d25c1"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("74ff801f-58f8-4fc6-8faf-bdddecd4673e"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("7eb34403-7641-49aa-8a0c-7f30a39d2355"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("84810cdc-4299-42f1-903d-8fff82ed4e92"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("909f7e6d-451c-4bff-b8d5-bfb04f3b5116"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("a4cbac96-a44d-4823-9924-e4a530ee96b2"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("a5792083-b9e7-4ef7-8f3f-b9835d908362"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("b256a4b1-86fe-46db-ab50-0061e7854996"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("d9c3ac3e-052c-4992-ac36-bd3079499663"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("e708632b-d307-4045-9778-679d979b1578"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("ecb72690-91c9-43ae-9039-92ada963271c"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("eec22dfc-d996-4563-9e26-dd671f3057e2"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("f51fc05c-924b-4f6d-b0e0-8193b676a6f5"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("f82da976-2c10-428c-a192-6ebee06107ab"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("fa660eb9-810a-47c1-8010-799481c4dca5"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "AppTypeCapability",
                keyColumn: "Id",
                keyValue: new Guid("fcd84bc2-a09c-4e1b-a1e8-2a660a0d3113"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("0ba21247-bf12-4487-b2ad-e4c84a784d75"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("0c66bd0a-d6b1-419d-83c2-9186d773cc52"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("1f642072-7975-4fa0-8109-4d9be5ffa909"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("2661f347-6af3-40a6-a0c4-57fb7e4d1f72"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("2bc51a48-5e56-4c27-958a-615009fea233"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("30e21343-9124-4bec-b247-f780a5be12df"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("6e77364e-d15c-4a02-b12d-9d8a0d623ff3"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("76e18bcb-2ee9-4c9c-9d11-fb10c8f20ee0"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("8460d864-4df5-423d-bb46-c351594b6667"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "Capability",
                keyColumn: "Id",
                keyValue: new Guid("b4fdd637-4e1f-46fb-a5fe-83bccf14a30e"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "DiscoveryStrategy",
                keyColumn: "Id",
                keyValue: new Guid("506cfcc2-b806-43c3-9baf-593053d9826e"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "DiscoveryStrategy",
                keyColumn: "Id",
                keyValue: new Guid("59e2a600-f739-470b-9981-cfe538af272b"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "DiscoveryStrategy",
                keyColumn: "Id",
                keyValue: new Guid("f3ee2904-4e01-483f-ad94-e8237953fcfc"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "ProcessState",
                keyColumn: "Id",
                keyValue: new Guid("0d98c241-6630-4f0f-a16e-bad8a013eb31"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "ProcessState",
                keyColumn: "Id",
                keyValue: new Guid("2cd5e4ed-ac0e-40eb-abad-3b56819f97a4"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "ProcessState",
                keyColumn: "Id",
                keyValue: new Guid("517b2fb1-e5f9-4fdd-98de-10cedec5bcc3"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "ProcessState",
                keyColumn: "Id",
                keyValue: new Guid("55331f23-8eae-4e56-811d-8bbdeaca03ca"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "ProcessState",
                keyColumn: "Id",
                keyValue: new Guid("b9057b8a-7fe7-407c-84e2-dcdc41caeee1"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "ProcessState",
                keyColumn: "Id",
                keyValue: new Guid("d413f4ed-d764-4277-ab4c-190822d22789"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "RestartPolicy",
                keyColumn: "Id",
                keyValue: new Guid("16657ec0-d027-497e-9bbf-eb835492f80b"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "RestartPolicy",
                keyColumn: "Id",
                keyValue: new Guid("89ff36ea-1f8a-42a2-a02d-b33b3dd2918c"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "RestartPolicy",
                keyColumn: "Id",
                keyValue: new Guid("efecc013-d65f-407c-af7a-ab61043e00b2"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "ServeMode",
                keyColumn: "Id",
                keyValue: new Guid("47a7a3f0-9ab5-4194-8b1a-ee1267cb844c"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);

            migrationBuilder.UpdateData(
                table: "ServeMode",
                keyColumn: "Id",
                keyValue: new Guid("56d426ec-c60b-449f-a62a-f294bb893fda"),
                columns: ["CreatedAt", "UpdatedAt"],
                values: [new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)]);
        }
    }
}
