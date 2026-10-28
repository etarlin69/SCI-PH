using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCI_PH.Persistence.Migrations.Clinical
{
    /// <inheritdoc />
    public partial class AddFingerprintEnrollmentSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PatientBiometricReferences_Provider_Modality_SubjectReferen~",
                schema: "clinical",
                table: "PatientBiometricReferences");

            migrationBuilder.CreateTable(
                name: "FingerprintEnrollmentSessions",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceCredentialId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByStaffId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<int>(type: "integer", nullable: true),
                    ResultHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    BiometricReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FingerprintEnrollmentSessions", x => x.Id);
                    table.CheckConstraint("CK_FingerprintEnrollmentSessions_Dates", "(\"ExpiresAtUtc\" > \"CreatedAtUtc\") AND (\"ResolvedAtUtc\" >= \"CreatedAtUtc\")");
                    table.CheckConstraint("CK_FingerprintEnrollmentSessions_FailureReason_Enum", "\"FailureReason\" IN (1, 2, 3, 4, 5, 6, 7)");
                    table.CheckConstraint("CK_FingerprintEnrollmentSessions_Lifecycle", "((\"ResolvedAtUtc\" IS NULL) = (\"ResultHash\" IS NULL)) AND (((\"Status\" IN (1, 4, 5)) AND (\"ResolvedAtUtc\" IS NULL) AND (\"BiometricReferenceId\" IS NULL) AND (\"FailureReason\" IS NULL)) OR ((\"Status\" = 2) AND (\"ResolvedAtUtc\" IS NOT NULL) AND (\"BiometricReferenceId\" IS NOT NULL) AND (\"FailureReason\" IS NULL)) OR ((\"Status\" = 3) AND (\"ResolvedAtUtc\" IS NOT NULL) AND (\"BiometricReferenceId\" IS NULL) AND (\"FailureReason\" IS NOT NULL)))");
                    table.CheckConstraint("CK_FingerprintEnrollmentSessions_ResultHash", "(\"ResultHash\" IS NULL) OR (length(\"ResultHash\") = 64)");
                    table.CheckConstraint("CK_FingerprintEnrollmentSessions_Status_Enum", "\"Status\" IN (1, 2, 3, 4, 5)");
                    table.ForeignKey(
                        name: "FK_FingerprintEnrollmentSessions_ClinicalStaffMembers_Requeste~",
                        column: x => x.RequestedByStaffId,
                        principalSchema: "clinical",
                        principalTable: "ClinicalStaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FingerprintEnrollmentSessions_PatientBiometricReferences_Bi~",
                        column: x => x.BiometricReferenceId,
                        principalSchema: "clinical",
                        principalTable: "PatientBiometricReferences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FingerprintEnrollmentSessions_Patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "clinical",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_PatientBiometricReferences_ActiveSubject",
                schema: "clinical",
                table: "PatientBiometricReferences",
                columns: new[] { "Provider", "Modality", "SubjectReference" },
                unique: true,
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_FingerprintEnrollmentSessions_BiometricReferenceId",
                schema: "clinical",
                table: "FingerprintEnrollmentSessions",
                column: "BiometricReferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_FingerprintEnrollmentSessions_RequestedByStaffId",
                schema: "clinical",
                table: "FingerprintEnrollmentSessions",
                column: "RequestedByStaffId");

            migrationBuilder.CreateIndex(
                name: "UX_FingerprintEnrollmentSessions_PendingPerPatient",
                schema: "clinical",
                table: "FingerprintEnrollmentSessions",
                column: "PatientId",
                unique: true,
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "UX_FingerprintEnrollmentSessions_PendingPerReader",
                schema: "clinical",
                table: "FingerprintEnrollmentSessions",
                column: "DeviceCredentialId",
                unique: true,
                filter: "\"Status\" = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FingerprintEnrollmentSessions",
                schema: "clinical");

            migrationBuilder.DropIndex(
                name: "UX_PatientBiometricReferences_ActiveSubject",
                schema: "clinical",
                table: "PatientBiometricReferences");

            migrationBuilder.CreateIndex(
                name: "IX_PatientBiometricReferences_Provider_Modality_SubjectReferen~",
                schema: "clinical",
                table: "PatientBiometricReferences",
                columns: new[] { "Provider", "Modality", "SubjectReference" },
                unique: true);
        }
    }
}
