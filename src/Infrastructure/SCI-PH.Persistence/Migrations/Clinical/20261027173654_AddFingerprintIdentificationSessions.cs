using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCI_PH.Persistence.Migrations.Clinical
{
    /// <inheritdoc />
    public partial class AddFingerprintIdentificationSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FingerprintIdentificationSessions",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicalEncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceCredentialId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByStaffId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    MatchedBiometricReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    FailureReason = table.Column<int>(type: "integer", nullable: true),
                    ResultHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConfirmedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FingerprintIdentificationSessions", x => x.Id);
                    table.CheckConstraint("CK_FingerprintIdentificationSessions_Dates", "(\"ExpiresAtUtc\" > \"CreatedAtUtc\") AND (\"ResolvedAtUtc\" >= \"CreatedAtUtc\") AND (\"ConfirmedAtUtc\" >= \"ResolvedAtUtc\")");
                    table.CheckConstraint("CK_FingerprintIdentificationSessions_FailureReason_Enum", "\"FailureReason\" IN (1, 2, 3, 4, 5, 6, 7)");
                    table.CheckConstraint("CK_FingerprintIdentificationSessions_Lifecycle", "((\"ResolvedAtUtc\" IS NULL) = (\"ResultHash\" IS NULL)) AND (((\"Status\" = 1) AND (\"ResolvedAtUtc\" IS NULL) AND (\"MatchedBiometricReferenceId\" IS NULL) AND (\"FailureReason\" IS NULL) AND (\"ConfirmedAtUtc\" IS NULL)) OR ((\"Status\" = 2) AND (\"ResolvedAtUtc\" IS NOT NULL) AND (\"MatchedBiometricReferenceId\" IS NOT NULL) AND (\"FailureReason\" IS NULL) AND (\"ConfirmedAtUtc\" IS NULL)) OR ((\"Status\" IN (3, 4)) AND (\"ResolvedAtUtc\" IS NOT NULL) AND (\"MatchedBiometricReferenceId\" IS NULL) AND (\"FailureReason\" IS NOT NULL) AND (\"ConfirmedAtUtc\" IS NULL)) OR ((\"Status\" = 7) AND (\"ResolvedAtUtc\" IS NOT NULL) AND (\"MatchedBiometricReferenceId\" IS NOT NULL) AND (\"FailureReason\" IS NULL) AND (\"ConfirmedAtUtc\" IS NOT NULL)) OR ((\"Status\" IN (5, 6)) AND ((\"ResolvedAtUtc\" IS NULL) = (\"MatchedBiometricReferenceId\" IS NULL)) AND (\"FailureReason\" IS NULL) AND (\"ConfirmedAtUtc\" IS NULL)))");
                    table.CheckConstraint("CK_FingerprintIdentificationSessions_ResultHash", "(\"ResultHash\" IS NULL) OR (length(\"ResultHash\") = 64)");
                    table.CheckConstraint("CK_FingerprintIdentificationSessions_Status_Enum", "\"Status\" IN (1, 2, 3, 4, 5, 6, 7)");
                    table.ForeignKey(
                        name: "FK_FingerprintIdentificationSessions_ClinicalEncounters_Clinic~",
                        column: x => x.ClinicalEncounterId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalEncounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FingerprintIdentificationSessions_ClinicalStaffMembers_Requ~",
                        column: x => x.RequestedByStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FingerprintIdentificationSessions_PatientBiometricReference~",
                        column: x => x.MatchedBiometricReferenceId,
                        principalSchema: "clinical",
                        principalTable: "PatientBiometricReferences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FingerprintIdentificationSessions_MatchedBiometricReference~",
                schema: "clinical",
                table: "FingerprintIdentificationSessions",
                column: "MatchedBiometricReferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_FingerprintIdentificationSessions_RequestedByStaffId",
                schema: "clinical",
                table: "FingerprintIdentificationSessions",
                column: "RequestedByStaffId");

            migrationBuilder.CreateIndex(
                name: "UX_FingerprintIdentificationSessions_ActivePerEncounter",
                schema: "clinical",
                table: "FingerprintIdentificationSessions",
                column: "ClinicalEncounterId",
                unique: true,
                filter: "\"Status\" IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "UX_FingerprintIdentificationSessions_PendingPerReader",
                schema: "clinical",
                table: "FingerprintIdentificationSessions",
                column: "DeviceCredentialId",
                unique: true,
                filter: "\"Status\" = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FingerprintIdentificationSessions",
                schema: "clinical");
        }
    }
}
