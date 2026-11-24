namespace ConfReg.Domain.Abstractions
{
    /// <summary>Who is calling. Replaces Request.UserHostAddress. Null when there is no caller (a background job).</summary>
    public interface IClientInfo
    {
        string IpAddress { get; }
    }
}
