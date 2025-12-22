using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Email.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddPushNotificationSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "IncludedPushLimit",
                table: "UsagePeriods",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastPushStripeReportUtc",
                table: "UsagePeriods",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OveragePush",
                table: "UsagePeriods",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "OveragePushReportedToStripe",
                table: "UsagePeriods",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "PushSent",
                table: "UsagePeriods",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "AllowsPushOverage",
                table: "BillingPlans",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "IncludedPush",
                table: "BillingPlans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxPushCredentials",
                table: "BillingPlans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PushOverageRateCentsPer1K",
                table: "BillingPlans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PushCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Platform = table.Column<byte>(type: "tinyint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ApplicationId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    EncryptedCredentials = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    KeyId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TeamId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AwsApplicationArn = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    StatusMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ValidatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushCredentials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PushTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    DefaultDataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PushDeviceTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CredentialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AwsEndpointArn = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Token = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Platform = table.Column<byte>(type: "tinyint", nullable: false),
                    ExternalUserId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UnregisteredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushDeviceTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PushDeviceTokens_PushCredentials_CredentialId",
                        column: x => x.CredentialId,
                        principalTable: "PushCredentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PushMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CredentialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExternalUserId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    DataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PlatformOptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AwsMessageId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    TargetCount = table.Column<int>(type: "int", nullable: false),
                    DeliveredCount = table.Column<int>(type: "int", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ScheduledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PushMessages_PushCredentials_CredentialId",
                        column: x => x.CredentialId,
                        principalTable: "PushCredentials",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PushMessages_PushDeviceTokens_DeviceTokenId",
                        column: x => x.DeviceTokenId,
                        principalTable: "PushDeviceTokens",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PushMessages_PushTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "PushTemplates",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PushEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PushMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeviceToken = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PushEvents_PushMessages_PushMessageId",
                        column: x => x.PushMessageId,
                        principalTable: "PushMessages",
                        principalColumn: "Id");
                });

            migrationBuilder.UpdateData(
                table: "BillingPlans",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                columns: new[] { "AllowsPushOverage", "IncludedPush", "MaxPushCredentials", "PushOverageRateCentsPer1K" },
                values: new object[] { true, 0, 2, 100 });

            migrationBuilder.UpdateData(
                table: "BillingPlans",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000002"),
                columns: new[] { "AllowsPushOverage", "IncludedPush", "MaxPushCredentials", "PushOverageRateCentsPer1K" },
                values: new object[] { true, 0, 2, 100 });

            migrationBuilder.UpdateData(
                table: "BillingPlans",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000003"),
                columns: new[] { "AllowsPushOverage", "IncludedPush", "MaxPushCredentials", "PushOverageRateCentsPer1K" },
                values: new object[] { true, 0, 2, 100 });

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

            migrationBuilder.CreateIndex(
                name: "IX_PushCredentials_AwsArn",
                table: "PushCredentials",
                column: "AwsApplicationArn");

            migrationBuilder.CreateIndex(
                name: "IX_PushCredentials_TenantDefault",
                table: "PushCredentials",
                columns: new[] { "TenantId", "IsDefault" });

            migrationBuilder.CreateIndex(
                name: "UQ_PushCredentials_TenantName",
                table: "PushCredentials",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushDeviceTokens_AwsArn",
                table: "PushDeviceTokens",
                column: "AwsEndpointArn");

            migrationBuilder.CreateIndex(
                name: "IX_PushDeviceTokens_TenantUser",
                table: "PushDeviceTokens",
                columns: new[] { "TenantId", "ExternalUserId" });

            migrationBuilder.CreateIndex(
                name: "UQ_PushDeviceTokens_CredentialToken",
                table: "PushDeviceTokens",
                columns: new[] { "CredentialId", "Token" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushEvents_MessageId",
                table: "PushEvents",
                column: "PushMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_PushEvents_Tenant_Time",
                table: "PushEvents",
                columns: new[] { "TenantId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PushMessages_AwsMessageId",
                table: "PushMessages",
                column: "AwsMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_PushMessages_CredentialId",
                table: "PushMessages",
                column: "CredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_PushMessages_DeviceTokenId",
                table: "PushMessages",
                column: "DeviceTokenId");

            migrationBuilder.CreateIndex(
                name: "IX_PushMessages_Scheduled",
                table: "PushMessages",
                columns: new[] { "Status", "ScheduledAtUtc" },
                filter: "[Status] = 4");

            migrationBuilder.CreateIndex(
                name: "IX_PushMessages_TemplateId",
                table: "PushMessages",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_PushMessages_Tenant_Time",
                table: "PushMessages",
                columns: new[] { "TenantId", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UQ_PushTemplates_TenantName",
                table: "PushTemplates",
                columns: new[] { "TenantId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PushEvents");

            migrationBuilder.DropTable(
                name: "PushMessages");

            migrationBuilder.DropTable(
                name: "PushDeviceTokens");

            migrationBuilder.DropTable(
                name: "PushTemplates");

            migrationBuilder.DropTable(
                name: "PushCredentials");

            migrationBuilder.DropColumn(
                name: "IncludedPushLimit",
                table: "UsagePeriods");

            migrationBuilder.DropColumn(
                name: "LastPushStripeReportUtc",
                table: "UsagePeriods");

            migrationBuilder.DropColumn(
                name: "OveragePush",
                table: "UsagePeriods");

            migrationBuilder.DropColumn(
                name: "OveragePushReportedToStripe",
                table: "UsagePeriods");

            migrationBuilder.DropColumn(
                name: "PushSent",
                table: "UsagePeriods");

            migrationBuilder.DropColumn(
                name: "AllowsPushOverage",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "IncludedPush",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "MaxPushCredentials",
                table: "BillingPlans");

            migrationBuilder.DropColumn(
                name: "PushOverageRateCentsPer1K",
                table: "BillingPlans");

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
        }
    }
}
