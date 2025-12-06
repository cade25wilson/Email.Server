using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Email.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddEntraUserFieldsToTenantMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TenantMembers_AspNetUsers_UserId",
                table: "TenantMembers");

            migrationBuilder.AddColumn<string>(
                name: "UserDisplayName",
                table: "TenantMembers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserEmail",
                table: "TenantMembers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "ap-southeast-2",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 2, 5, 24, 32, 311, DateTimeKind.Utc).AddTicks(6011));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "eu-west-1",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 2, 5, 24, 32, 311, DateTimeKind.Utc).AddTicks(6009));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "us-east-1",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 2, 5, 24, 32, 311, DateTimeKind.Utc).AddTicks(4014));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "us-west-2",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 2, 5, 24, 32, 311, DateTimeKind.Utc).AddTicks(6006));

            migrationBuilder.CreateIndex(
                name: "IX_TenantMembers_Email",
                table: "TenantMembers",
                column: "UserEmail");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TenantMembers_Email",
                table: "TenantMembers");

            migrationBuilder.DropColumn(
                name: "UserDisplayName",
                table: "TenantMembers");

            migrationBuilder.DropColumn(
                name: "UserEmail",
                table: "TenantMembers");

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "ap-southeast-2",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 11, 30, 22, 52, 9, 973, DateTimeKind.Utc).AddTicks(2965));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "eu-west-1",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 11, 30, 22, 52, 9, 973, DateTimeKind.Utc).AddTicks(2948));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "us-east-1",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 11, 30, 22, 52, 9, 972, DateTimeKind.Utc).AddTicks(8989));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "us-west-2",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 11, 30, 22, 52, 9, 973, DateTimeKind.Utc).AddTicks(2929));

            migrationBuilder.AddForeignKey(
                name: "FK_TenantMembers_AspNetUsers_UserId",
                table: "TenantMembers",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
