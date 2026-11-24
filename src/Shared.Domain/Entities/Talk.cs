using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ConfReg.Domain.Entities
{
    public class Talk
    {
        public int Id { get; set; }

        [Required, StringLength(200)]
        public string Title { get; set; }

        [Required, StringLength(150)]
        public string Speaker { get; set; }

        [Required, StringLength(50)]
        public string Track { get; set; }

        public DateTime StartsUtc { get; set; }

        public int DurationMinutes { get; set; }

        [Required, StringLength(50)]
        public string Room { get; set; }

        public int Capacity { get; set; }

        [NotMapped]
        public DateTime EndsUtc
        {
            get { return StartsUtc.AddMinutes(DurationMinutes); }
        }

        public virtual ICollection<RegistrationTalk> RegistrationTalks { get; set; }
    }
}
