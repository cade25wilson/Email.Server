using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Email.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSmsPools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PoolId",
                table: "SmsPhoneNumbers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SmsPools",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AwsPoolId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AwsPoolArn = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    PoolName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsPools", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SmsPools_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "ap-southeast-2",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 29, 1, 20, 5, 842, DateTimeKind.Utc).AddTicks(7842));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "eu-west-1",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 29, 1, 20, 5, 842, DateTimeKind.Utc).AddTicks(7840));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "us-east-1",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 29, 1, 20, 5, 842, DateTimeKind.Utc).AddTicks(5722));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "us-west-2",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 29, 1, 20, 5, 842, DateTimeKind.Utc).AddTicks(7838));

            migrationBuilder.CreateIndex(
                name: "IX_SmsPhoneNumbers_PoolId",
                table: "SmsPhoneNumbers",
                column: "PoolId");

            migrationBuilder.CreateIndex(
                name: "IX_SmsPools_AwsPoolId",
                table: "SmsPools",
                column: "AwsPoolId");

            migrationBuilder.CreateIndex(
                name: "UQ_SmsPools_Tenant",
                table: "SmsPools",
                column: "TenantId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SmsPhoneNumbers_SmsPools_PoolId",
                table: "SmsPhoneNumbers",
                column: "PoolId",
                principalTable: "SmsPools",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SmsPhoneNumbers_SmsPools_PoolId",
                table: "SmsPhoneNumbers");

            migrationBuilder.DropTable(
                name: "SmsPools");

            migrationBuilder.DropIndex(
                name: "IX_SmsPhoneNumbers_PoolId",
                table: "SmsPhoneNumbers");

            migrationBuilder.DropColumn(
                name: "PoolId",
                table: "SmsPhoneNumbers");

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "ap-southeast-2",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 21, 21, 27, 45, 327, DateTimeKind.Utc).AddTicks(3688));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "eu-west-1",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 21, 21, 27, 45, 327, DateTimeKind.Utc).AddTicks(3673));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "us-east-1",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 21, 21, 27, 45, 326, DateTimeKind.Utc).AddTicks(8236));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "us-west-2",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 21, 21, 27, 45, 327, DateTimeKind.Utc).AddTicks(3640));
        }
    }
}
