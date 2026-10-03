using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCI_PH.Persistence.Migrations.Identity
{
    /// <inheritdoc />
    public partial class IdentitySecurityFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "EmailIndex",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                schema: "identity",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: false,
                // Existing accounts get the migration time instead of year 1.
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeactivatedAtUtc",
                schema: "identity",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                schema: "identity",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                // Existing accounts stay active (and satisfy CK_AspNetUsers_Deactivation).
                defaultValue: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastLoginAtUtc",
                schema: "identity",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByIp = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedByIp = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    RevocationReason = table.Column<int>(type: "integer", nullable: true),
                    ReplacedByTokenId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.CheckConstraint("CK_RefreshTokens_Expiration", "\"ExpiresAtUtc\" > \"CreatedAtUtc\"");
                    table.CheckConstraint("CK_RefreshTokens_Hash", "\"TokenHash\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_RefreshTokens_Revocation", "((\"RevokedAtUtc\" IS NULL) = (\"RevocationReason\" IS NULL)) AND (\"RevokedAtUtc\" >= \"CreatedAtUtc\") AND (NOT (\"ReplacedByTokenId\" IS NOT NULL) OR (\"RevocationReason\" = 1))");
                    table.CheckConstraint("CK_RefreshTokens_RevocationReason_Enum", "\"RevocationReason\" IN (1, 2, 3, 4, 5, 6)");
                    table.ForeignKey(
                        name: "FK_RefreshTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_RefreshTokens_ReplacedByTokenId",
                        column: x => x.ReplacedByTokenId,
                        principalSchema: "identity",
                        principalTable: "RefreshTokens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SecurityAuditEvents",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<int>(type: "integer", nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityAuditEvents", x => x.Id);
                    table.CheckConstraint("CK_SecurityAuditEvents_EventType_Enum", "\"EventType\" IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14)");
                    table.CheckConstraint("CK_SecurityAuditEvents_FailureReason_Enum", "\"FailureReason\" IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10)");
                    table.CheckConstraint("CK_SecurityAuditEvents_Outcome", "((\"Outcome\" = 1) AND (\"FailureReason\" IS NULL)) OR ((\"Outcome\" = 2) AND (\"FailureReason\" IS NOT NULL))");
                    table.CheckConstraint("CK_SecurityAuditEvents_Outcome_Enum", "\"Outcome\" IN (1, 2)");
                });

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "identity",
                table: "AspNetUsers",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AspNetUsers_Deactivation",
                schema: "identity",
                table: "AspNetUsers",
                sql: "\"IsActive\" = (\"DeactivatedAtUtc\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_FamilyId",
                schema: "identity",
                table: "RefreshTokens",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_ReplacedByTokenId",
                schema: "identity",
                table: "RefreshTokens",
                column: "ReplacedByTokenId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                schema: "identity",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId_ExpiresAtUtc",
                schema: "identity",
                table: "RefreshTokens",
                columns: new[] { "UserId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId_RevokedAtUtc",
                schema: "identity",
                table: "RefreshTokens",
                columns: new[] { "UserId", "RevokedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAuditEvents_EventType_OccurredAtUtc",
                schema: "identity",
                table: "SecurityAuditEvents",
                columns: new[] { "EventType", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAuditEvents_OccurredAtUtc",
                schema: "identity",
                table: "SecurityAuditEvents",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAuditEvents_UserId_OccurredAtUtc",
                schema: "identity",
                table: "SecurityAuditEvents",
                columns: new[] { "UserId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RefreshTokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "SecurityAuditEvents",
                schema: "identity");

            migrationBuilder.DropIndex(
                name: "EmailIndex",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AspNetUsers_Deactivation",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "DeactivatedAtUtc",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "IsActive",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LastLoginAtUtc",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "identity",
                table: "AspNetUsers",
                column: "NormalizedEmail");
        }
    }
}
