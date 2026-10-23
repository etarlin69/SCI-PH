using SCI_PH.Application;
using SCI_PH.Infrastructure;
using SCI_PH.Infrastructure.Web;
using SCI_PH.Persistence;

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
builder.Services.AddApplication().AddAccountSecurity().AddClinicalWorkflows();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration)
    .AddJwtSecurity(builder.Configuration)
    .AddSecurityRateLimiting(builder.Configuration);

var app = builder.Build();

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
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

await app.RunAsync();
