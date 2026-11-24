using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ConfReg.Domain.Entities
{
    public class Attendee
    {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string FirstName { get; set; }

        [Required, StringLength(100)]
        public string LastName { get; set; }

        [Required, StringLength(256)]
        public string Email { get; set; }

        [StringLength(150)]
        public string Company { get; set; }

        [StringLength(150)]
        public string JobTitle { get; set; }

        [StringLength(500)]
        public string DietaryRequirements { get; set; }

        public DateTime CreatedUtc { get; set; }

        public virtual ICollection<Registration> Registrations { get; set; }
    }
}
