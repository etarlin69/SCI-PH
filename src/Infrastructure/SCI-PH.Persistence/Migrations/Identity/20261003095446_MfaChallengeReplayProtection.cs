using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCI_PH.Persistence.Migrations.Identity
{
    /// <inheritdoc />
    public partial class MfaChallengeReplayProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MfaLoginChallenges",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallengeHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfaLoginChallenges", x => x.Id);
                    table.CheckConstraint("CK_MfaLoginChallenges_Dates", "(\"ExpiresAtUtc\" > \"CreatedAtUtc\") AND (\"ConsumedAtUtc\" >= \"CreatedAtUtc\")");
                    table.CheckConstraint("CK_MfaLoginChallenges_Hash", "\"ChallengeHash\" ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_MfaLoginChallenges_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MfaLoginChallenges_ChallengeHash",
                schema: "identity",
                table: "MfaLoginChallenges",
                column: "ChallengeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MfaLoginChallenges_UserId_ExpiresAtUtc",
                schema: "identity",
                table: "MfaLoginChallenges",
                columns: new[] { "UserId", "ExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MfaLoginChallenges",
                schema: "identity");
        }
    }
}
