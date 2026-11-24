using ConfReg.Domain.Abstractions;

namespace ConfReg.Data.Infrastructure
{
    /// <summary>
    /// Creates a short-lived ConfRegContext per unit of work, as Phase 1 did with `new ConfRegContext()`, but through
    /// an object that is injected, so the connection string comes from ISettings and tests can point it elsewhere.
    /// </summary>
    public interface IConfRegContextFactory
    {
        ConfRegContext Create();
    }

    public class ConfRegContextFactory : IConfRegContextFactory
    {
        private readonly ISettings _settings;
        public ConfRegContextFactory(ISettings settings) { _settings = settings; }
        public ConfRegContext Create() { return new ConfRegContext(_settings.ConnectionString); }
    }
}
