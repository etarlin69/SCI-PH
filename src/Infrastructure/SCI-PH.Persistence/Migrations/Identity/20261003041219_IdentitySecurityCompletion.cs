using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SCI_PH.Persistence.Migrations.Identity
{
    /// <inheritdoc />
    public partial class IdentitySecurityCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityAuditEvents_EventType_Enum",
                schema: "identity",
                table: "SecurityAuditEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityAuditEvents_FailureReason_Enum",
                schema: "identity",
                table: "SecurityAuditEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RefreshTokens_RevocationReason_Enum",
                schema: "identity",
                table: "RefreshTokens");

            migrationBuilder.AddColumn<Guid>(
                name: "ActorUserId",
                schema: "identity",
                table: "SecurityAuditEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeviceCredentialId",
                schema: "identity",
                table: "SecurityAuditEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AccountInvitations",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectType = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IntendedRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TokenHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcceptedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountInvitations", x => x.Id);
                    table.CheckConstraint("CK_AccountInvitations_Email", "length(btrim(\"Email\")) > 0");
                    table.CheckConstraint("CK_AccountInvitations_Expiration", "\"ExpiresAtUtc\" > \"CreatedAtUtc\"");
                    table.CheckConstraint("CK_AccountInvitations_Hash", "\"TokenHash\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_AccountInvitations_Lifecycle", "((\"Status\" = 1) AND (\"AcceptedAtUtc\" IS NULL) AND (\"AcceptedByUserId\" IS NULL) AND (\"RevokedAtUtc\" IS NULL)) OR ((\"Status\" = 2) AND (\"AcceptedAtUtc\" IS NOT NULL) AND (\"RevokedAtUtc\" IS NULL)) OR ((\"Status\" = 3) AND (\"RevokedAtUtc\" IS NOT NULL) AND (\"AcceptedByUserId\" IS NULL))");
                    table.CheckConstraint("CK_AccountInvitations_Role", "((\"SubjectType\" = 1) AND (\"IntendedRole\" = 'Patient')) OR ((\"SubjectType\" = 2) AND (\"IntendedRole\" IN ('Doctor', 'Nurse', 'Paramedic', 'Receptionist')))");
                    table.CheckConstraint("CK_AccountInvitations_Status_Enum", "\"Status\" IN (1, 2, 3)");
                    table.CheckConstraint("CK_AccountInvitations_SubjectType_Enum", "\"SubjectType\" IN (1, 2)");
                });

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeviceCredentials",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Scopes = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SecretHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FirmwareVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RotatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceCredentials", x => x.Id);
                    table.CheckConstraint("CK_DeviceCredentials_Code", "\"DeviceCode\" ~ '^[A-Z0-9][A-Z0-9-]{2,59}$'");
                    table.CheckConstraint("CK_DeviceCredentials_Hash", "\"SecretHash\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_DeviceCredentials_Name", "length(btrim(\"Name\")) > 0");
                    table.CheckConstraint("CK_DeviceCredentials_Revocation", "((\"Status\" = 1) AND (\"RevokedAtUtc\" IS NULL)) OR ((\"Status\" = 2) AND (\"RevokedAtUtc\" IS NOT NULL))");
                    table.CheckConstraint("CK_DeviceCredentials_Scopes", "\"Scopes\" ~ '^Device[.][A-Za-z.]+( Device[.][A-Za-z.]+)*$'");
                    table.CheckConstraint("CK_DeviceCredentials_Status_Enum", "\"Status\" IN (1, 2)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAuditEvents_DeviceCredentialId_OccurredAtUtc",
                schema: "identity",
                table: "SecurityAuditEvents",
                columns: new[] { "DeviceCredentialId", "OccurredAtUtc" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecurityAuditEvents_EventType_Enum",
                schema: "identity",
                table: "SecurityAuditEvents",
                sql: "\"EventType\" IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecurityAuditEvents_FailureReason_Enum",
                schema: "identity",
                table: "SecurityAuditEvents",
                sql: "\"FailureReason\" IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RefreshTokens_RevocationReason_Enum",
                schema: "identity",
                table: "RefreshTokens",
                sql: "\"RevocationReason\" IN (1, 2, 3, 4, 5, 6, 7, 8, 9)");

            migrationBuilder.CreateIndex(
                name: "IX_AccountInvitations_Email",
                schema: "identity",
                table: "AccountInvitations",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_AccountInvitations_TokenHash",
                schema: "identity",
                table: "AccountInvitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_AccountInvitations_PendingSubject",
                schema: "identity",
                table: "AccountInvitations",
                columns: new[] { "SubjectType", "SubjectId" },
                unique: true,
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceCredentials_DeviceCode",
                schema: "identity",
                table: "DeviceCredentials",
                column: "DeviceCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountInvitations",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "DataProtectionKeys",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "DeviceCredentials",
                schema: "identity");

            migrationBuilder.DropIndex(
                name: "IX_SecurityAuditEvents_DeviceCredentialId_OccurredAtUtc",
                schema: "identity",
                table: "SecurityAuditEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityAuditEvents_EventType_Enum",
                schema: "identity",
                table: "SecurityAuditEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityAuditEvents_FailureReason_Enum",
                schema: "identity",
                table: "SecurityAuditEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RefreshTokens_RevocationReason_Enum",
                schema: "identity",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "ActorUserId",
                schema: "identity",
                table: "SecurityAuditEvents");

            migrationBuilder.DropColumn(
                name: "DeviceCredentialId",
                schema: "identity",
                table: "SecurityAuditEvents");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecurityAuditEvents_EventType_Enum",
                schema: "identity",
                table: "SecurityAuditEvents",
                sql: "\"EventType\" IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecurityAuditEvents_FailureReason_Enum",
                schema: "identity",
                table: "SecurityAuditEvents",
                sql: "\"FailureReason\" IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RefreshTokens_RevocationReason_Enum",
                schema: "identity",
                table: "RefreshTokens",
                sql: "\"RevocationReason\" IN (1, 2, 3, 4, 5, 6)");
        }
    }
}
