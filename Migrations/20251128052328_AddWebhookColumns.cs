using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Email.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EventTypes",
                table: "WebhookEndpoints",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "WebhookEndpoints",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextRetryAtUtc",
                table: "WebhookDeliveries",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseBody",
                table: "WebhookDeliveries",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResponseStatusCode",
                table: "WebhookDeliveries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WebhookEndpointsId",
                table: "WebhookDeliveries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "ap-southeast-2",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 11, 28, 5, 23, 25, 225, DateTimeKind.Utc).AddTicks(843));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "eu-west-1",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 11, 28, 5, 23, 25, 225, DateTimeKind.Utc).AddTicks(840));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "us-east-1",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 11, 28, 5, 23, 25, 224, DateTimeKind.Utc).AddTicks(6722));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "us-west-2",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 11, 28, 5, 23, 25, 225, DateTimeKind.Utc).AddTicks(834));

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_WebhookEndpointsId",
                table: "WebhookDeliveries",
                column: "WebhookEndpointsId");

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookDeliveries_WebhookEndpoints_WebhookEndpointsId",
                table: "WebhookDeliveries",
                column: "WebhookEndpointsId",
                principalTable: "WebhookEndpoints",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WebhookDeliveries_WebhookEndpoints_WebhookEndpointsId",
                table: "WebhookDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_WebhookDeliveries_WebhookEndpointsId",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "EventTypes",
                table: "WebhookEndpoints");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "WebhookEndpoints");

            migrationBuilder.DropColumn(
                name: "NextRetryAtUtc",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "ResponseBody",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "ResponseStatusCode",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "WebhookEndpointsId",
                table: "WebhookDeliveries");

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "ap-southeast-2",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 11, 26, 20, 13, 21, 75, DateTimeKind.Utc).AddTicks(1949));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "eu-west-1",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 11, 26, 20, 13, 21, 75, DateTimeKind.Utc).AddTicks(1947));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "us-east-1",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 11, 26, 20, 13, 21, 74, DateTimeKind.Utc).AddTicks(8988));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "us-west-2",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 11, 26, 20, 13, 21, 75, DateTimeKind.Utc).AddTicks(1944));
        }
    }
}
