using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCI_PH.Persistence.Migrations.Clinical
{
    /// <inheritdoc />
    public partial class AddFaceTemplateDeletionJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FaceTemplateDeletionJobs",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BiometricReferenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastFailure = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaceTemplateDeletionJobs", x => x.Id);
                    table.CheckConstraint("CK_FaceTemplateDeletionJobs_Attempts", "\"Attempts\" >= 0");
                    table.CheckConstraint("CK_FaceTemplateDeletionJobs_Dates", "(\"LastAttemptAtUtc\" >= \"CreatedAtUtc\") AND (\"CompletedAtUtc\" >= \"CreatedAtUtc\")");
                    table.CheckConstraint("CK_FaceTemplateDeletionJobs_LastFailure_Enum", "\"LastFailure\" IN (1, 2, 3)");
                    table.CheckConstraint("CK_FaceTemplateDeletionJobs_Lifecycle", "((\"Status\" = 1) AND (\"Outcome\" IS NULL) AND (\"CompletedAtUtc\" IS NULL)) OR ((\"Status\" = 2) AND (\"Outcome\" IS NOT NULL) AND (\"CompletedAtUtc\" IS NOT NULL))");
                    table.CheckConstraint("CK_FaceTemplateDeletionJobs_Outcome_Enum", "\"Outcome\" IN (1, 2)");
                    table.CheckConstraint("CK_FaceTemplateDeletionJobs_Status_Enum", "\"Status\" IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_FaceTemplateDeletionJobs_PatientBiometricReferences_Biometr~",
                        column: x => x.BiometricReferenceId,
                        principalSchema: "clinical",
                        principalTable: "PatientBiometricReferences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FaceTemplateDeletionJobs_Pending",
                schema: "clinical",
                table: "FaceTemplateDeletionJobs",
                column: "CreatedAtUtc",
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "UX_FaceTemplateDeletionJobs_Reference",
                schema: "clinical",
                table: "FaceTemplateDeletionJobs",
                column: "BiometricReferenceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FaceTemplateDeletionJobs",
                schema: "clinical");
        }
    }
}
