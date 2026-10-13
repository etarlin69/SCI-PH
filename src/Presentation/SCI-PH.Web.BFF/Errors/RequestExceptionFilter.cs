using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Web.BFF.Errors
{
    // Central translation of expected failures into ProblemDetails. Only known exception types are mapped:
    // ArgumentException and InvalidOperationException are NOT mapped globally (Application translates the Domain's
    // argument guards itself), so a programming error still ends as a 500 from UseExceptionHandler.
    public sealed class RequestExceptionFilter : IExceptionFilter
    {
        private const string ConcurrencyMessage = "The record was changed by another operation. Reload it and try again.";

        private readonly ProblemDetailsFactory _problems;
        private readonly ILogger<RequestExceptionFilter> _logger;

        public RequestExceptionFilter(ProblemDetailsFactory problems, ILogger<RequestExceptionFilter> logger)
        {
            _problems = problems;
            _logger = logger;
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

        private static (int Status, string Title, string? Detail)? Classify(Exception exception) =>
            exception switch
            {
                RequestValidationException => (StatusCodes.Status400BadRequest, "The request is not valid.", exception.Message),
                AccessDeniedException => (StatusCodes.Status403Forbidden, "Access denied.", exception.Message),
                ResourceNotFoundException => (StatusCodes.Status404NotFound, "The resource was not found.", exception.Message),
                ResourceConflictException => (StatusCodes.Status409Conflict, "The request conflicts with the current state.", exception.Message),
                DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "The request conflicts with the current state.", ConcurrencyMessage),
                // Domain state rules are conflicts; any other Domain rule is invalid input (e.g. a vital sign out of range).
                PatientAlreadyIdentifiedException or InvalidClinicalEncounterStateException =>
                    (StatusCodes.Status409Conflict, "The request conflicts with the current state.", exception.Message),
                DomainException => (StatusCodes.Status400BadRequest, "The request is not valid.", exception.Message),
                _ => null
            };
    }
}
