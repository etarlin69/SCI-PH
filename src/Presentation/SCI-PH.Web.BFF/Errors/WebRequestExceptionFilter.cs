using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SCI_PH.Infrastructure.Web;

namespace SCI_PH.Web.BFF.Errors
{
    // The shared mapping (RequestExceptionFilter) plus an EF Core concurrency failure that a store did not translate.
    // Infrastructure does not reference EF Core, so this fallback lives in the host.
    public sealed class WebRequestExceptionFilter : RequestExceptionFilter
    {
        private const string ConcurrencyMessage = "The record was changed by another operation. Reload it and try again.";

        public WebRequestExceptionFilter(ProblemDetailsFactory problems, ILoggerFactory loggers)
            : base(problems, loggers)
        {
        }

        protected override (int Status, string Title, string? Detail)? Classify(Exception exception) =>
            exception is DbUpdateConcurrencyException
                ? (StatusCodes.Status409Conflict, "The request conflicts with the current state.", ConcurrencyMessage)
                : base.Classify(exception);
    }
}
