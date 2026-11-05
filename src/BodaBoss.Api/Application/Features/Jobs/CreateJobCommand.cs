using BodaBoss.Api.Data;
using BodaBoss.Api.Domain;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BodaBoss.Api.Application.Features.Jobs;

public sealed record CreateJobCommand(
    Guid TenantId,
    Guid CreatedByUserId,
    string PickupLocation,
    string DropoffLocation,
    string? Instructions,
    long AmountUgx,
    DateTimeOffset Deadline,
    Guid? RiderId) : IRequest<FleetJob>;

public sealed class CreateJobCommandValidator : AbstractValidator<CreateJobCommand>
{
    public CreateJobCommandValidator()
    {
        RuleFor(command => command.PickupLocation).NotEmpty().MaximumLength(240);
        RuleFor(command => command.DropoffLocation).NotEmpty().MaximumLength(240);
        RuleFor(command => command.Instructions).MaximumLength(2000);
        RuleFor(command => command.AmountUgx).GreaterThanOrEqualTo(0);
        RuleFor(command => command.Deadline).GreaterThan(DateTimeOffset.UtcNow);
        RuleFor(command => command.RiderId).NotEmpty().When(command => command.RiderId.HasValue);
    }
}

public sealed class CreateJobCommandHandler(AppDbContext db)
    : IRequestHandler<CreateJobCommand, FleetJob>
{
    public async Task<FleetJob> Handle(CreateJobCommand command, CancellationToken cancellationToken)
    {
        if (command.RiderId is Guid riderId && !await db.Drivers.AnyAsync(
            rider => rider.Id == riderId && rider.Status == "Active",
            cancellationToken))
        {
            throw new KeyNotFoundException("The selected active rider was not found in this fleet.");
        }

        var job = new FleetJob
        {
            TenantId = command.TenantId,
            CreatedByUserId = command.CreatedByUserId,
            PickupLocation = command.PickupLocation,
            DropoffLocation = command.DropoffLocation,
            Instructions = command.Instructions,
            AmountUgx = command.AmountUgx,
            Deadline = command.Deadline,
            RiderId = command.RiderId,
            Status = command.RiderId.HasValue ? "Assigned" : "Unassigned"
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);
        return job;
    }
}
