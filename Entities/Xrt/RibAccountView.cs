using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AfbGenerator.Api.Entities.Xrt
{
    [Keyless]
    [Table("RIBAccount")]
    public class RibAccountView
    {
        [Column("ACCOUNTCODE")]
        public string? AccountCode { get; set; }

        [Column("DESCRIPTION")]
        public string? Description { get; set; }

        [Column("ACCOUNTLOCALRIB")]
        public string? AccountLocalRib { get; set; }

        [Column("DEVISE")]
        public string? Devise { get; set; }
    }
}