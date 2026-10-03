using System.Reflection;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Exceptions;
using SCI_PH.Domain.ValueObjects;

namespace SCI_PH.Domain.Tests
{
    public class DomainConventionTests
    {
        private static readonly Assembly DomainAssembly = typeof(Patient).Assembly;

        public static TheoryData<Type> DomainEnums()
        {
            var data = new TheoryData<Type>();
            foreach (var type in DomainAssembly.GetTypes().Where(t => t.IsEnum))
            {
                data.Add(type);
            }

            return data;
        }

        [Theory]
        [MemberData(nameof(DomainEnums))]
        public void EnumsStartAtOneSoTheDefaultValueIsNeverValid(Type enumType)
        {
            var values = Enum.GetValuesAsUnderlyingType(enumType).Cast<object>().Select(Convert.ToInt32).ToList();

            Assert.DoesNotContain(0, values);
            Assert.Equal(values.Count, values.Distinct().Count());
        }

        [Fact]
        public void DomainHasNoTechnologyDependencies()
        {
            var references = DomainAssembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

            Assert.DoesNotContain(references, name =>
                name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                || name.StartsWith("Npgsql", StringComparison.Ordinal)
                || name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                || name.StartsWith("Azure", StringComparison.Ordinal)
                || name.StartsWith("SCI-PH.", StringComparison.Ordinal));
        }

        [Fact]
        public void EntitiesDoNotExposePublicSetters()
        {
            var entities = DomainAssembly.GetTypes().Where(t => t.Namespace?.StartsWith("SCI_PH.Domain.Entities", StringComparison.Ordinal) == true && t.IsClass && !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)));

            Assert.All(entities.SelectMany(t => t.GetProperties()), property =>
                Assert.False(property.SetMethod?.IsPublic == true && !IsInitOnly(property), $"{property.DeclaringType!.Name}.{property.Name}"));
        }

        [Fact]
        public void TemporaryPatientCodeRoundTrips()
        {
            var code = TemporaryPatientCode.New();

            Assert.Equal(code, TemporaryPatientCode.Parse(code.Value.ToLowerInvariant()));
            Assert.Throws<DomainException>(() => TemporaryPatientCode.Parse("NN-123"));
        }

        [Fact]
        public void ChecksumMustBeSha256Hex()
        {
            Assert.Throws<DomainException>(() => Sha256Checksum.Parse("abc"));
            Assert.Throws<DomainException>(() => Sha256Checksum.Parse(new string('g', Sha256Checksum.Length)));
        }

        private static bool IsInitOnly(PropertyInfo property) =>
            property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers()
                .Any(modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit");
    }
}
