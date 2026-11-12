using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Application.Common.Idempotency;
using SCI_PH.Persistence.Clinical.Idempotency;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Idempotency
{
    // Applied explicitly by ClinicalDbContext (it is not a Domain entity). The unique key is what makes a concurrent
    // double submit safe: the second transaction fails on it and rolls back everything it tried to create.
    internal sealed class IdempotencyReceiptConfiguration : IEntityTypeConfiguration<IdempotencyReceiptEntry>
    {
        public const string UniqueKeyName = "UX_IdempotencyReceipts_Key";

        public void Configure(EntityTypeBuilder<IdempotencyReceiptEntry> builder)
        {
            builder.ToTable("IdempotencyReceipts");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Operation).HasMaxLength(IdempotencyReceiptEntry.OperationMaxLength);
            builder.Property(x => x.RequestHash).HasMaxLength(IdempotencyRequestHash.Length).IsFixedLength();

            builder.HasIndex(x => new { x.ActorUserId, x.Operation, x.ClientRequestId })
                .IsUnique()
                .HasDatabaseName(UniqueKeyName);

            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_IdempotencyReceipts_Operation", NotBlank(nameof(IdempotencyReceiptEntry.Operation)));
                table.HasCheckConstraint("CK_IdempotencyReceipts_RequestHash",
                    $"length({Column(nameof(IdempotencyReceiptEntry.RequestHash))}) = {IdempotencyRequestHash.Length}");
            });
        }
    }
}
