namespace ConfReg.Domain.Security
{
    public interface IPasswordHasher
    {
        string Hash(string password);
        bool Verify(string password, string hash);

        /// <summary>True when the stored value is already in the current format (BCrypt), false for anything older.</summary>
        bool IsCurrentFormat(string stored);
    }
}
