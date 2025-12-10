using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Email.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddInboundEmailSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "S3ObjectKey",
                table: "InboundMessages",
                newName: "BlobKey");

            migrationBuilder.AddColumn<Guid>(
                name: "DomainId",
                table: "InboundMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessedAtUtc",
                table: "InboundMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SesMessageId",
                table: "InboundMessages",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SizeBytes",
                table: "InboundMessages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "InboundEnabled",
                table: "Domains",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte>(
                name: "InboundStatus",
                table: "Domains",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "ap-southeast-2",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 10, 6, 49, 32, 283, DateTimeKind.Utc).AddTicks(4218));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "eu-west-1",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 10, 6, 49, 32, 283, DateTimeKind.Utc).AddTicks(4216));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "us-east-1",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 10, 6, 49, 32, 283, DateTimeKind.Utc).AddTicks(924));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "us-west-2",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 10, 6, 49, 32, 283, DateTimeKind.Utc).AddTicks(4212));

            migrationBuilder.CreateIndex(
                name: "IX_InboundMessages_DomainId",
                table: "InboundMessages",
                column: "DomainId");

            migrationBuilder.AddForeignKey(
                name: "FK_InboundMessages_Domains_DomainId",
                table: "InboundMessages",
                column: "DomainId",
                principalTable: "Domains",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InboundMessages_Domains_DomainId",
                table: "InboundMessages");

            migrationBuilder.DropIndex(
                name: "IX_InboundMessages_DomainId",
                table: "InboundMessages");

            migrationBuilder.DropColumn(
                name: "DomainId",
                table: "InboundMessages");

            migrationBuilder.DropColumn(
                name: "ProcessedAtUtc",
                table: "InboundMessages");

            migrationBuilder.DropColumn(
                name: "SesMessageId",
                table: "InboundMessages");

            migrationBuilder.DropColumn(
                name: "SizeBytes",
                table: "InboundMessages");

            migrationBuilder.DropColumn(
                name: "InboundEnabled",
                table: "Domains");

            migrationBuilder.DropColumn(
                name: "InboundStatus",
                table: "Domains");

            migrationBuilder.RenameColumn(
                name: "BlobKey",
                table: "InboundMessages",
                newName: "S3ObjectKey");

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "ap-southeast-2",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 9, 6, 37, 18, 367, DateTimeKind.Utc).AddTicks(6645));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "eu-west-1",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 9, 6, 37, 18, 367, DateTimeKind.Utc).AddTicks(6643));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "us-east-1",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 9, 6, 37, 18, 367, DateTimeKind.Utc).AddTicks(2348));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "us-west-2",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 9, 6, 37, 18, 367, DateTimeKind.Utc).AddTicks(6639));
        }
    }
}
