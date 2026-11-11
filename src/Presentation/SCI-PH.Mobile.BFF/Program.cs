using SCI_PH.Application;
using SCI_PH.Infrastructure;
using SCI_PH.Infrastructure.Web;
using SCI_PH.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using SCI_PH.Infrastructure.Observability;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Errores esperados (validación, acceso, no encontrado, conflicto) como ProblemDetails; el resto sigue a UseExceptionHandler.
builder.Services.AddControllers(options => options.Filters.Add<RequestExceptionFilter>());
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options => options.AddBearerSecurity());

// La app MAUI no es un navegador: no usa CORS ni cookies; guarda el refresh token en SecureStorage.
// AddClinicalWorkflows: emergencia prehospitalaria del paramédico (api/mobile/emergencies).
// AddPatientSelfService: expediente propio del paciente, solo lectura (api/mobile/patient/me, ADR-015).
builder.Services.AddApplication().AddAccountSecurity().AddClinicalWorkflows().AddPatientSelfService();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration)
    .AddJwtSecurity(builder.Configuration)
    .AddSecurityRateLimiting(builder.Configuration);

var app = builder.Build();
app.UseRequestObservability();

// Configure the HTTP request pipeline.
// Errores no controlados y respuestas 4xx/5xx sin cuerpo se devuelven como ProblemDetails (RFC 7807).
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.MapOpenApi().AllowAnonymous();
}
else
{
    // En desarrollo se permite HTTP local; fuera de desarrollo se exige HTTPS.
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseApiSecurityHeaders();
// La autenticación va primero: el límite del autoservicio del paciente se parte por usuario autenticado (JWT "sub").
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
// Liveness only verifies that the ASP.NET process can answer requests.
app.MapHealthChecks(
        "/health/live",
        new HealthCheckOptions
        {
            Predicate = _ => false
        })
    .AllowAnonymous();

// Readiness verifies the dependencies required to serve traffic.
app.MapHealthChecks(
        "/health/ready",
        new HealthCheckOptions
        {
            Predicate = healthCheck =>
                healthCheck.Tags.Contains("ready")
        })
    .AllowAnonymous();

// Keep the existing endpoint for compatibility.
app.MapHealthChecks(
        "/health",
        new HealthCheckOptions
        {
            Predicate = _ => false
        })
    .AllowAnonymous();

await app.RunAsync();
