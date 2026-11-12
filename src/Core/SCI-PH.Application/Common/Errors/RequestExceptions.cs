namespace SCI_PH.Application.Common.Errors
{
    // Expected request failures raised by Application use cases. The presentation layer maps each type to
    // a ProblemDetails status (400, 403, 404, 409); anything else stays an unexpected 500.

    // Invalid input: missing or malformed values, a date in the future, a value the Domain rejects.
    public sealed class RequestValidationException : Exception
    {
        public RequestValidationException(string message, string? field = null, Exception? innerException = null)
            : base(message, innerException)
        {
            Field = field;
        }

        public string? Field { get; }
    }

    // The caller is authenticated but may not perform the operation on this resource.
    public sealed class AccessDeniedException : Exception
    {
        public AccessDeniedException(string message) : base(message) { }
    }

    public sealed class ResourceNotFoundException : Exception
    {
        public ResourceNotFoundException(string resource, Guid id)
            : base($"{resource} {id} was not found.")
        {
            Resource = resource;
        }

        // For a lookup by a sensitive value (a document number): the message never repeats it.
        public ResourceNotFoundException(string resource, string message)
            : base(message)
        {
            Resource = resource;
        }

        public string Resource { get; }
    }

    // The request is valid but conflicts with the current state: a concurrent change, a uniqueness rule
    // or a state the operation does not accept.
    public sealed class ResourceConflictException : Exception
    {
        public ResourceConflictException(string message, Exception? innerException = null) : base(message, innerException) { }
    }

    // An external dependency the request needs is not available now (a biometric provider that is down, timed out or
    // failed internally): 503. The message is generic; the cause stays in the inner exception, never in the response.
    public sealed class ExternalServiceUnavailableException : Exception
    {
        public ExternalServiceUnavailableException(string message, Exception? innerException = null) : base(message, innerException) { }
    }
}
