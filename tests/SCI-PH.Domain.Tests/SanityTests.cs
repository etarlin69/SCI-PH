using System.Reflection;

namespace SCI_PH.Domain.Tests
{
    public class SanityTests
    {
        [Fact]
        public void DomainAssembly_CanBeLoaded()
        {
            var assembly = Assembly.Load("SCI-PH.Domain");

            Assert.NotNull(assembly);
        }
    }
}
