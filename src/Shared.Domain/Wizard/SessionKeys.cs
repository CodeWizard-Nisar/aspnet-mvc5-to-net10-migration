using System;

namespace ConfReg.Domain.Wizard
{
    /// <summary>
    /// The names and types of every session value that is shared between Legacy.Web and Modern.Web. THIS LIST IS THE
    /// CONTRACT. Both apps register exactly these keys with SystemWebAdapters, from this one definition, because a key
    /// registered on only one side is not an error: the value is silently dropped, and "it works on my machine" follows.
    /// Add a key here and both apps pick it up. See docs/05-strangler.md.
    /// </summary>
    public static class SessionKeys
    {
        public const string AdminUserId = "AdminUserId";
        public const string AdminEmail = "AdminEmail";
        public const string AdminRole = "AdminRole";
        public const string Wizard = "ConfReg.Wizard";

        public static readonly Tuple<string, Type>[] Shared =
        {
            Tuple.Create(AdminUserId, typeof(int)),
            Tuple.Create(AdminEmail, typeof(string)),
            Tuple.Create(AdminRole, typeof(string)),
            Tuple.Create(Wizard, typeof(WizardSnapshot)),
        };
    }
}
