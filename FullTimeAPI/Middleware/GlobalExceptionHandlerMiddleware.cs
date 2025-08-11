using System.Net;
using System.Text.Json;

namespace FullTimeAPI.Middleware
{
    public class GlobalExceptionHandlerMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

        public GlobalExceptionHandlerMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            _logger.LogError(exception, "An unhandled exception occurred: {Message}", exception.Message);

            var response = context.Response;
            response.ContentType = "application/json";

            var errorResponse = new
            {
                error = new
                {
                    message = "An error occurred while processing your request.",
                    type = exception.GetType().Name,
                    timestamp = DateTime.UtcNow
                }
            };

            switch (exception)
            {
                case ArgumentNullException:
                case ArgumentException:
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    errorResponse = new
                    {
                        error = new
                        {
                            message = exception.Message,
                            type = exception.GetType().Name,
                            timestamp = DateTime.UtcNow
                        }
                    };
                    break;
                case HttpRequestException:
                    response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                    errorResponse = new
                    {
                        error = new
                        {
                            message = "External service temporarily unavailable. Please try again later.",
                            type = "ServiceUnavailable",
                            timestamp = DateTime.UtcNow
                        }
                    };
                    break;
                case TaskCanceledException when ((TaskCanceledException)exception).CancellationToken.IsCancellationRequested:
                    response.StatusCode = (int)HttpStatusCode.RequestTimeout;
                    errorResponse = new
                    {
                        error = new
                        {
                            message = "Request timeout occurred.",
                            type = "RequestTimeout",
                            timestamp = DateTime.UtcNow
                        }
                    };
                    break;
                default:
                    response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    break;
            }

            var jsonResponse = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await response.WriteAsync(jsonResponse);
        }
    }
}