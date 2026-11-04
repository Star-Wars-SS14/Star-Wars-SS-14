using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database
{
    /// <summary>
    /// A player's progress in one season. Numbers worth reading in a query are columns; the lists live in
    /// <see cref="State"/> as JSON, so a new quest or counter needs no migration. No foreign key on the player,
    /// as in <see cref="PlayTime"/>: an admin can grant to a player who has not joined yet.
    /// </summary>
    [Table("sw_battle_pass_progress")]
    [PrimaryKey(nameof(PlayerUserId), nameof(SeasonId))]
    public sealed class SWBattlePassProgress
    {
        [Required]
        public Guid PlayerUserId { get; set; }

        [Required]
        public string SeasonId { get; set; } = default!;

        public int Xp { get; set; }

        public bool Paid { get; set; }

        [Required]
        public string State { get; set; } = "{}";

        public DateTime UpdatedAt { get; set; }
    }
}
