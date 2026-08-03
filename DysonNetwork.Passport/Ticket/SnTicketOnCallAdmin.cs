using System.ComponentModel.DataAnnotations.Schema;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Passport.Ticket;

[Index(nameof(AccountId), IsUnique = true)]
public class SnTicketOnCallAdmin : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AccountId { get; set; }

    [NotMapped] public SnAccount? Account { get; set; }
}
