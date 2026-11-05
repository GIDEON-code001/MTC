using FluentValidation;
using MediatR;

namespace BodaBoss.Api.Application.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var failures = validators
            .Select(validator => validator.ValidateAsync(request, cancellationToken))
            .ToArray();
        var results = await Task.WhenAll(failures);
        var errors = results
            .SelectMany(result => result.Errors)
            .Where(error => error is not null)
            .ToArray();
        if (errors.Length > 0)
        {
            throw new ValidationException(errors);
        }

        return await next();
    }
}
