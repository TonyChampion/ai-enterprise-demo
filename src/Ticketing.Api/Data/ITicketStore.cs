using Ticketing.Api.Domain;

namespace Ticketing.Api.Data;

/// <summary>
/// The only way the API reaches ticket data. In-memory today, a database tomorrow.
/// </summary>
public interface ITicketStore
{
    Task<Ticket?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Ticket?> GetByNumberAsync(int number, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Ticket>> ListAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Ticket ticket, CancellationToken cancellationToken = default);
    Task UpdateAsync(Ticket ticket, CancellationToken cancellationToken = default);
}
