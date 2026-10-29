using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Medical.Api.Infrastructure;

/// <summary>
/// Runs the registered FluentValidation validator for every bound action argument and
/// short-circuits with a 400 ValidationProblemDetails when any rule fails.
/// </summary>
public sealed class ValidationFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var ct = context.HttpContext.RequestAborted;
        var errors = new Dictionary<string, List<string>>();

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null || argument is CancellationToken) continue;
            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (services.GetService(validatorType) is not IValidator validator) continue;

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), ct);
            foreach (var failure in result.Errors)
            {
                var key = ProblemDetailsSetup.ToFieldKey(failure.PropertyName);
                if (!errors.TryGetValue(key, out var list)) errors[key] = list = [];
                list.Add(failure.ErrorMessage);
            }
        }

        if (errors.Count == 0)
        {
            await next();
            return;
        }

        var problem = new ValidationProblemDetails(errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = ProblemDetailsSetup.ValidationTitle,
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            Detail = string.Join(" ", errors.Values.SelectMany(v => v).Distinct()),
        };
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        context.Result = new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    }
}
