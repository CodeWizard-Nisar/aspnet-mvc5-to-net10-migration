using System;
using System.Collections.Generic;
using System.Web.Mvc;
using ConfReg.Data.Configuration;
using ConfReg.Data.Infrastructure;
using ConfReg.Data.Services;
using ConfReg.Domain.Abstractions;
using ConfReg.Domain.Security;
using ConfReg.Domain.Services;
using ConfReg.Domain.Wizard;
using ConfReg.Legacy.Controllers;

namespace ConfReg.Legacy.Infrastructure
{
    /// <summary>
    /// The composition root: the one place that decides which implementation backs each interface.
    /// Phase 1's controllers each `new`ed their own services (BASELINE-PROBLEM #5, "no DI"). They now take them
    /// through their constructors and this class supplies them.
    ///
    /// Deliberately hand-written, no DI container. A container adds assemblies (and, on .NET Framework, binding
    /// redirects) to a legacy app for no benefit while the whole graph fits on one screen; the modern host gets
    /// Microsoft.Extensions.DependencyInjection for free in Phase 5. Everything here is a singleton because every
    /// service is stateless: each unit of work asks the context factory for a fresh DbContext.
    /// </summary>
    public class LegacyComposition : IDependencyResolver
    {
        private readonly Dictionary<Type, Func<object>> _map = new Dictionary<Type, Func<object>>();

        public static LegacyComposition Build(IPasswordHasher hasher)
        {
            // The only call that reads Web.config, secrets.config and the environment.
            ISettings settings = new ConfRegSettings(AppConfigLoader.Load());

            var db = new ConfRegContextFactory(settings);
            var session = new HttpSessionStore();
            var wizard = new WizardSession(session);
            var prices = new SettingsService(db, settings);
            var gateway = new PaymentGateway(settings, new HttpClientInfo(), new ServerAppPaths());
            var registrations = new RegistrationService(db, settings, prices, gateway);
            var reports = new ReportQueries(db);
            var auth = new AdminAuthService(db, hasher, new LegacyPasswordCipher(settings));

            var c = new LegacyComposition();
            // Controllers are transient (MVC makes one per request); the services they share are the singletons above.
            c._map[typeof(HomeController)] = () => new HomeController();
            c._map[typeof(RegisterController)] = () => new RegisterController(registrations, prices, wizard, db, settings);
            c._map[typeof(AccountController)] = () => new AccountController(auth);
            c._map[typeof(AdminController)] = () => new AdminController(db, registrations, reports, prices);
            c._map[typeof(AttendeesController)] = () => new AttendeesController(db);
            c._map[typeof(TalksController)] = () => new TalksController(db, registrations);
            c._map[typeof(RegistrationsController)] = () => new RegistrationsController(db, gateway);
            return c;
        }

        public object GetService(Type serviceType)
        {
            Func<object> factory;
            return _map.TryGetValue(serviceType, out factory) ? factory() : null; // null: MVC falls back to its default
        }

        public IEnumerable<object> GetServices(Type serviceType)
        {
            var one = GetService(serviceType);
            return one == null ? new object[0] : new[] { one };
        }
    }
}
