namespace SCI_PH.IntegrationTests
{
    public class SanityTests
    {
        [Fact]
        public void ReferencedAssemblies_CanBeLoaded()
        {
            Assert.Equal("SCI-PH.Application", typeof(SCI_PH.Application.DependencyInjection).Assembly.GetName().Name);
            Assert.Equal("SCI-PH.Persistence", typeof(SCI_PH.Persistence.DependencyInjection).Assembly.GetName().Name);
            Assert.Equal("SCI-PH.Infrastructure", typeof(SCI_PH.Infrastructure.DependencyInjection).Assembly.GetName().Name);
        }
    }
}
