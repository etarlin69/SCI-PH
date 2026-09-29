namespace SCI_PH.Application.Tests
{
    public class SanityTests
    {
        [Fact]
        public void ApplicationAssembly_CanBeLoaded()
        {
            var assembly = typeof(SCI_PH.Application.DependencyInjection).Assembly;

            Assert.Equal("SCI-PH.Application", assembly.GetName().Name);
        }
    }
}
