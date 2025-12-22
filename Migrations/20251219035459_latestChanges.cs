using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Email.Server.Migrations
{
    /// <inheritdoc />
    public partial class latestChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ_Suppressions",
                table: "Suppressions");

            migrationBuilder.AddColumn<long>(
                name: "IncludedSmsLimit",
                table: "UsagePeriods",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "OverageSms",
                table: "UsagePeriods",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "OverageSmsReportedToStripe",
                table: "UsagePeriods",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "SmsSegmentsSent",
                table: "UsagePeriods",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "SmsSent",
                table: "UsagePeriods",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Suppressions",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(320)",
                oldMaxLength: 320);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "Suppressions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "Type",
                table: "Suppressions",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<bool>(
                name: "AllowsSmsOverage",
                table: "BillingPlans",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "IncludedSms",
                table: "BillingPlans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SmsOverageRateCentsPer100",
                table: "BillingPlans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "SmsPhoneNumbers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PhoneNumberArn = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    NumberType = table.Column<byte>(type: "tinyint", nullable: false),
                    Country = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    MonthlyFeeCents = table.Column<int>(type: "int", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ProvisionedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsPhoneNumbers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SmsPhoneNumbers_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SmsTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(1600)", maxLength: 1600, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SmsTemplates_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SmsMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PhoneNumberId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FromNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ToNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(1600)", maxLength: 1600, nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AwsMessageId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    SegmentCount = table.Column<int>(type: "int", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ScheduledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SmsMessages_SmsPhoneNumbers_PhoneNumberId",
                        column: x => x.PhoneNumberId,
                        principalTable: "SmsPhoneNumbers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SmsMessages_SmsTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "SmsTemplates",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SmsMessages_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SmsEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SmsMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SmsEvents_SmsMessages_SmsMessageId",
                        column: x => x.SmsMessageId,
                        principalTable: "SmsMessages",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SmsEvents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.UpdateData(
                table: "BillingPlans",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                columns: new[] { "AllowsSmsOverage", "IncludedSms", "SmsOverageRateCentsPer100" },
                values: new object[] { true, 0, 150 });

            migrationBuilder.UpdateData(
                table: "BillingPlans",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000002"),
                columns: new[] { "AllowsSmsOverage", "IncludedSms", "SmsOverageRateCentsPer100" },
                values: new object[] { true, 0, 150 });

            migrationBuilder.UpdateData(
                table: "BillingPlans",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000003"),
                columns: new[] { "AllowsSmsOverage", "IncludedSms", "SmsOverageRateCentsPer100" },
                values: new object[] { true, 0, 150 });

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "ap-southeast-2",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 19, 3, 54, 58, 110, DateTimeKind.Utc).AddTicks(5215));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "eu-west-1",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 19, 3, 54, 58, 110, DateTimeKind.Utc).AddTicks(5213));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "us-east-1",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 19, 3, 54, 58, 110, DateTimeKind.Utc).AddTicks(3755));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "us-west-2",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 19, 3, 54, 58, 110, DateTimeKind.Utc).AddTicks(5211));

            migrationBuilder.CreateIndex(
                name: "IX_Suppressions_Tenant_Phone",
                table: "Suppressions",
                columns: new[] { "TenantId", "PhoneNumber" },
                filter: "[Type] = 1");

            migrationBuilder.CreateIndex(
                name: "UQ_Suppressions",
                table: "Suppressions",
                columns: new[] { "TenantId", "Region", "Email" },
                unique: true,
                filter: "[Region] IS NOT NULL AND [Type] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SmsEvents_MessageId",
                table: "SmsEvents",
                column: "SmsMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_SmsEvents_Tenant_Time",
                table: "SmsEvents",
                columns: new[] { "TenantId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SmsMessages_AwsMessageId",
                table: "SmsMessages",
                column: "AwsMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_SmsMessages_PhoneNumberId",
                table: "SmsMessages",
                column: "PhoneNumberId");

            migrationBuilder.CreateIndex(
                name: "IX_SmsMessages_Scheduled",
                table: "SmsMessages",
                columns: new[] { "Status", "ScheduledAtUtc" },
                filter: "[Status] = 4");

            migrationBuilder.CreateIndex(
                name: "IX_SmsMessages_TemplateId",
                table: "SmsMessages",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_SmsMessages_Tenant_Time",
                table: "SmsMessages",
                columns: new[] { "TenantId", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SmsPhoneNumbers_TenantDefault",
                table: "SmsPhoneNumbers",
                columns: new[] { "TenantId", "IsDefault" });

            migrationBuilder.CreateIndex(
                name: "UQ_SmsPhoneNumbers_Number",
                table: "SmsPhoneNumbers",
                column: "PhoneNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_SmsTemplates_TenantName",
                table: "SmsTemplates",
                columns: new[] { "TenantId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SmsEvents");

            migrationBuilder.DropTable(
                name: "SmsMessages");

            migrationBuilder.DropTable(
                name: "SmsPhoneNumbers");

            migrationBuilder.DropTable(
                name: "SmsTemplates");

            migrationBuilder.DropIndex(
                name: "IX_Suppressions_Tenant_Phone",
                table: "Suppressions");

            migrationBuilder.DropIndex(
                name: "UQ_Suppressions",
                table: "Suppressions");

            migrationBuilder.DropColumn(
                name: "IncludedSmsLimit",
                table: "UsagePeriods");

            migrationBuilder.DropColumn(
                name: "OverageSms",
                table: "UsagePeriods");

            migrationBuilder.DropColumn(
                name: "OverageSmsReportedToStripe",
                table: "UsagePeriods");

            migrationBuilder.DropColumn(
                name: "SmsSegmentsSent",
                table: "UsagePeriods");

            migrationBuilder.DropColumn(
                name: "SmsSent",
                table: "UsagePeriods");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "Suppressions");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Suppressions");

            migrationBuilder.DropColumn(
                name: "AllowsSmsOverage",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "IncludedSms",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "SmsOverageRateCentsPer100",
                table: "BillingPlans");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Suppressions",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(320)",
                oldMaxLength: 320,
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "ap-southeast-2",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 17, 3, 46, 27, 693, DateTimeKind.Utc).AddTicks(2174));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "eu-west-1",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 17, 3, 46, 27, 693, DateTimeKind.Utc).AddTicks(2172));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "us-east-1",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 17, 3, 46, 27, 692, DateTimeKind.Utc).AddTicks(7322));

            migrationBuilder.UpdateData(
                table: "RegionsCatalog",
                keyColumn: "Region",
                keyValue: "us-west-2",
                column: "CreatedAtUtc",
                value: new DateTime(2025, 12, 17, 3, 46, 27, 693, DateTimeKind.Utc).AddTicks(2163));

            migrationBuilder.CreateIndex(
                name: "UQ_Suppressions",
                table: "Suppressions",
                columns: new[] { "TenantId", "Region", "Email" },
                unique: true,
                filter: "[Region] IS NOT NULL");
        }
    }
}
