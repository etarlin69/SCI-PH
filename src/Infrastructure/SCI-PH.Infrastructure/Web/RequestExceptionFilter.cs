using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Infrastructure.Web
{
    // Central translation of expected failures into ProblemDetails, shared by the hosts. Only known exception types are
    // mapped: ArgumentException and InvalidOperationException are NOT mapped globally (Application translates the
    // Domain's argument guards itself), so a programming error still ends as a 500 from UseExceptionHandler.
    // A host may add mappings for its own dependencies by overriding Classify (Web BFF: EF Core concurrency).
    public class RequestExceptionFilter : IExceptionFilter
    {
        private readonly ProblemDetailsFactory _problems;
        private readonly ILogger _logger;

        public RequestExceptionFilter(ProblemDetailsFactory problems, ILoggerFactory loggers)
        {
            _problems = problems;
            _logger = loggers.CreateLogger(GetType());
        }

        public void OnException(ExceptionContext context)
        {
            if (Classify(context.Exception) is not { } mapped)
            {
                return;
            }

            var (status, title, detail) = mapped;
            // The type only: messages may echo request values.
            _logger.LogInformation("Request rejected with {StatusCode} ({ExceptionType}).", status, context.Exception.GetType().Name);

            var problem = _problems.CreateProblemDetails(context.HttpContext, status, title, detail: detail);
            if (context.Exception is RequestValidationException { Field: { } field })
            {
                problem.Extensions["field"] = field;
            }

            context.Result = new ObjectResult(problem)
            {
                StatusCode = status,
                ContentTypes = { "application/problem+json" }
            };
            context.ExceptionHandled = true;
        }

        protected virtual (int Status, string Title, string? Detail)? Classify(Exception exception) =>
            exception switch
            {
                RequestValidationException => (StatusCodes.Status400BadRequest, "The request is not valid.", exception.Message),
                AccessDeniedException => (StatusCodes.Status403Forbidden, "Access denied.", exception.Message),
                ResourceNotFoundException => (StatusCodes.Status404NotFound, "The resource was not found.", exception.Message),
                ResourceConflictException => (StatusCodes.Status409Conflict, "The request conflicts with the current state.", exception.Message),
                // Generic by construction: no provider, credential, model path or stack in the response.
                ExternalServiceUnavailableException => (StatusCodes.Status503ServiceUnavailable, "The service is temporarily unavailable.", exception.Message),
                // Domain state rules are conflicts; any other Domain rule is invalid input (e.g. a vital sign out of range).
                PatientAlreadyIdentifiedException or InvalidClinicalEncounterStateException =>
                    (StatusCodes.Status409Conflict, "The request conflicts with the current state.", exception.Message),
                DomainException => (StatusCodes.Status400BadRequest, "The request is not valid.", exception.Message),
                _ => null
            };
    }
}
