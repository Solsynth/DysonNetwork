using System;
using System.ComponentModel.DataAnnotations;

namespace DysonNetwork.Shared.Models
{
    public class SnUnpaidAccount
    {
        [Key]
        public Guid AccountId { get; set; }
        public DateTime MarkedAt { get; set; }
    }
}
