using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCI_PH.Persistence.Migrations.Clinical
{
    /// <inheritdoc />
    public partial class AddFingerprintSlotDeletionJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FingerprintSlotDeletionJobs",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BiometricReferenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceCredentialId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResultHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastFailure = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FingerprintSlotDeletionJobs", x => x.Id);
                    table.CheckConstraint("CK_FingerprintSlotDeletionJobs_Attempts", "(\"Attempts\" >= 0) AND ((\"LastAttemptId\" IS NULL) = (\"ResultHash\" IS NULL)) AND ((\"LastAttemptId\" IS NULL) = (\"LastAttemptAtUtc\" IS NULL)) AND (((\"Attempts\" = 0) AND (\"LastAttemptId\" IS NULL)) OR ((\"Attempts\" > 0) AND (\"LastAttemptId\" IS NOT NULL)))");
                    table.CheckConstraint("CK_FingerprintSlotDeletionJobs_Dates", "(\"LastAttemptAtUtc\" >= \"CreatedAtUtc\") AND (\"CompletedAtUtc\" >= \"CreatedAtUtc\")");
                    table.CheckConstraint("CK_FingerprintSlotDeletionJobs_LastFailure_Enum", "\"LastFailure\" IN (1)");
                    table.CheckConstraint("CK_FingerprintSlotDeletionJobs_Lifecycle", "((\"Status\" = 1) AND (\"Outcome\" IS NULL) AND (\"CompletedAtUtc\" IS NULL)) OR ((\"Status\" = 2) AND (\"Outcome\" IS NOT NULL) AND (\"CompletedAtUtc\" IS NOT NULL) AND (\"LastAttemptId\" IS NOT NULL))");
                    table.CheckConstraint("CK_FingerprintSlotDeletionJobs_Outcome_Enum", "\"Outcome\" IN (1, 2)");
                    table.CheckConstraint("CK_FingerprintSlotDeletionJobs_ResultHash", "(\"ResultHash\" IS NULL) OR (length(\"ResultHash\") = 64)");
                    table.CheckConstraint("CK_FingerprintSlotDeletionJobs_Status_Enum", "\"Status\" IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_FingerprintSlotDeletionJobs_PatientBiometricReferences_Biom~",
                        column: x => x.BiometricReferenceId,
                        principalSchema: "clinical",
                        principalTable: "PatientBiometricReferences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FingerprintSlotDeletionJobs_PendingPerReader",
                schema: "clinical",
                table: "FingerprintSlotDeletionJobs",
                columns: new[] { "DeviceCredentialId", "CreatedAtUtc" },
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "UX_FingerprintSlotDeletionJobs_Reference",
                schema: "clinical",
                table: "FingerprintSlotDeletionJobs",
                column: "BiometricReferenceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FingerprintSlotDeletionJobs",
                schema: "clinical");
        }
    }
}
