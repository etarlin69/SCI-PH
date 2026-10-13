using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCI_PH.Persistence.Migrations.Clinical
{
    /// <inheritdoc />
    public partial class EnforceSingleActiveTransferPerEncounter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_ClinicalTransfers_ActivePerEncounter",
                schema: "clinical",
                table: "ClinicalTransfers",
                column: "ClinicalEncounterId",
                unique: true,
                filter: "\"Status\" IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_ClinicalTransfers_ActivePerEncounter",
                schema: "clinical",
                table: "ClinicalTransfers");
        }
    }
}
