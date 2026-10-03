using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace SCI_PH.Persistence.Configurations.Clinical
{
    internal static class ClinicalModelConventions
    {
        // Deleting a patient, encounter or catalog row must never cascade into clinical history.
        public static ModelBuilder RestrictAllDeletes(this ModelBuilder modelBuilder)
        {
            foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetForeignKeys()))
            {
                foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
            }

            return modelBuilder;
        }

        // Rejects integers outside the C# enum (for example 0) at database level.
        // Adding an enum member therefore requires a migration, which keeps the database contract explicit.
        public static ModelBuilder AddEnumCheckConstraints(this ModelBuilder modelBuilder)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetDeclaredProperties())
                {
                    var enumType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                    if (!enumType.IsEnum)
                    {
                        continue;
                    }

                    var values = Enum.GetValuesAsUnderlyingType(enumType)
                        .Cast<object>()
                        .Select(value => Convert.ToString(value, CultureInfo.InvariantCulture));
                    var column = property.GetColumnName();
                    entityType.AddCheckConstraint(
                        $"CK_{entityType.GetTableName()}_{column}_Enum",
                        $"{Sql.Column(column)} IN ({string.Join(", ", values)})");
                }
            }

            return modelBuilder;
        }
    }
}
