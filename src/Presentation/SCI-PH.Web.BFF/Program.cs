using SCI_PH.Application;
using SCI_PH.Infrastructure;
using SCI_PH.Infrastructure.Web;
using SCI_PH.Persistence;
using SCI_PH.Web.BFF.Errors;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Errores esperados (validación, acceso, no encontrado, conflicto) como ProblemDetails; el resto sigue a UseExceptionHandler.
builder.Services.AddControllers(options => options.Filters.Add<WebRequestExceptionFilter>());
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options => options.AddBearerSecurity());

builder.Services.AddApplication().AddAccountSecurity().AddAccountManagement(builder.Configuration).AddClinicalWorkflows().AddFaceVerificationWorkflow().AddClinicalAiWorkflow();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration)
    .AddJwtSecurity(builder.Configuration)
    .AddEmailDelivery(builder.Configuration)
    .AddFrontendCors(builder.Configuration)
    .AddSecurityRateLimiting(builder.Configuration)
    .AddFaceVerification(builder.Configuration)
    .AddClinicalAi(builder.Configuration);

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
app.UseCors(FrontendCors.PolicyName);
// La autenticación va antes del rate limiting: la política de IA clínica se particiona por usuario autenticado.
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

// Roles y permisos (y el administrador inicial si está configurado fuera del repositorio).
// Solo se ejecuta con IdentityBootstrap:Enabled = true.
await app.Services.RunIdentityBootstrapAsync();

// Cuentas de prueba, una por rol (excepto Administrator). Solo en Development y con
// IdentityDevelopmentAccounts:Enabled = true; activada en otro entorno, el Web BFF no arranca.
await app.Services.RunIdentityDevelopmentAccountsAsync(app.Environment);

// Clínica y personal clínico de demostración para cuentas ya existentes. Solo en Development y con
// ClinicalDevelopmentSeed:Enabled = true (User Secrets); nunca en Production.
await app.Services.RunClinicalDevelopmentSeedAsync(app.Environment);

await app.RunAsync();
