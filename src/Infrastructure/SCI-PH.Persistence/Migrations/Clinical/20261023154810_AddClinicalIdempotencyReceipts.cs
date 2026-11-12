using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCI_PH.Persistence.Migrations.Clinical
{
    /// <inheritdoc />
    public partial class AddClinicalIdempotencyReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IdempotencyReceipts",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyReceipts", x => x.Id);
                    table.CheckConstraint("CK_IdempotencyReceipts_Operation", "length(btrim(\"Operation\")) > 0");
                    table.CheckConstraint("CK_IdempotencyReceipts_RequestHash", "length(\"RequestHash\") = 64");
                });

            migrationBuilder.CreateIndex(
                name: "UX_IdempotencyReceipts_Key",
                schema: "clinical",
                table: "IdempotencyReceipts",
                columns: new[] { "ActorUserId", "Operation", "ClientRequestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdempotencyReceipts",
                schema: "clinical");
        }
    }
}
