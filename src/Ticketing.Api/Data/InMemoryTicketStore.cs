using System.Collections.Concurrent;
using Ticketing.Api.Domain;

namespace Ticketing.Api.Data;

/// <summary>
/// Thread-safe in-memory store. Must be registered as a singleton:
/// any other lifetime gives each request (or scope) its own empty store.
/// </summary>
public class InMemoryTicketStore : ITicketStore
{
    private readonly ConcurrentDictionary<Guid, Ticket> _tickets = new();
    private int _nextNumber = SeedData.FirstNewNumber - 1;

    public InMemoryTicketStore() => Reset();

    /// <summary>Back to the seeded tickets. Demo page "Reset" button, between rehearsals.</summary>
    public void Reset()
    {
        _tickets.Clear();
        foreach (var ticket in SeedData.Tickets())
            _tickets[ticket.Id] = ticket;
        Interlocked.Exchange(ref _nextNumber, SeedData.FirstNewNumber - 1);
    }

    public Task<Ticket?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_tickets.TryGetValue(id, out var ticket) ? ticket : null);

    public Task<Ticket?> GetByNumberAsync(int number, CancellationToken cancellationToken = default)
        => Task.FromResult(_tickets.Values.FirstOrDefault(t => t.Number == number));

    public Task<IReadOnlyList<Ticket>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Ticket>>(_tickets.Values.OrderBy(t => t.Number).ToList());

    public Task AddAsync(Ticket ticket, CancellationToken cancellationToken = default)
    {
        ticket.Number = Interlocked.Increment(ref _nextNumber);
        _tickets[ticket.Id] = ticket;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Ticket ticket, CancellationToken cancellationToken = default)
    {
        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        _tickets[ticket.Id] = ticket;
        return Task.CompletedTask;
    }
}
