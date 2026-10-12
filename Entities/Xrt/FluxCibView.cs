using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AfbGenerator.Api.Entities.Xrt
{
    [Keyless]
    [Table("Flux_Cib")]
    public class FluxCibView
    {
        [Column("CODE")]
        public string? Code { get; set; }

        [Column("DESCRIPTION")]
        public string? Description { get; set; }

        [Column("flux")]
        public string? Flux { get; set; }

        [Column("CIB1")]
        public string? Cib1 { get; set; }

        [Column("CIB2")]
        public string? Cib2 { get; set; }

        [Column("ACC")]
        public string? Acc { get; set; }

        [Column("BQ")]
        public string? BanqueCode { get; set; }

        [Column("RIB")]
        public string? Rib { get; set; }
        [Column("CODE_BANQUE_RIB")]
        public string? CodeBanqueRib { get; set; } 
    }
}