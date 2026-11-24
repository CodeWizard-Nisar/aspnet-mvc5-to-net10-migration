using System.ComponentModel.DataAnnotations;

namespace ConfReg.Domain.Entities
{
    public class AdminUser
    {
        public int Id { get; set; }

        [Required, StringLength(256)]
        public string Email { get; set; }

        // BASELINE-PROBLEM #7: this column holds REVERSIBLE ciphertext, not a hash.
        // The name says "hash"; the content is AES output that anyone holding the key
        // (which lives in Web.config) can turn back into the plain password.
        // Fixed in Phase 3: BCrypt, re-hashed transparently on next login.
        [Required, StringLength(512)]
        public string PasswordHash { get; set; }

        [Required, StringLength(50)]
        public string Role { get; set; }
    }

    /// <summary>
    /// Not in the original entity list. Ticket prices and the open/close dates must be
    /// editable at runtime from the admin Settings page, so they need a home in the DB.
    /// </summary>
    public class Setting
    {
        [Key, StringLength(100)]
        public string Key { get; set; }

        [Required, StringLength(500)]
        public string Value { get; set; }
    }
}
