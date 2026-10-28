using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCI_PH.Persistence.Migrations.Clinical
{
    /// <inheritdoc />
    public partial class AddActiveFaceReferencePerPatientIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_PatientBiometricReferences_ActiveFacePerPatient",
                schema: "clinical",
                table: "PatientBiometricReferences",
                column: "PatientId",
                unique: true,
                filter: "(\"Modality\" = 2) AND (\"Status\" = 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_PatientBiometricReferences_ActiveFacePerPatient",
                schema: "clinical",
                table: "PatientBiometricReferences");
        }
    }
}
