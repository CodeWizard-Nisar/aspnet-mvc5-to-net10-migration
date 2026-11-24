using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ConfReg.Domain.Entities;

// Flat view models. They exist because MVC's built-in JSON serialiser (JavaScriptSerializer)
// throws on the circular graph that EF entities form, and because JSON is what the Phase 2
// characterization tests snapshot. Note the wire format: PascalCase property names and
// dates as "\/Date(1700000000000)\/". Phase 5 has to reproduce both exactly.
namespace ConfReg.Legacy.Models
{
    public class FormBase
    {
        public FormBase() { Errors = new List<string>(); }
        public List<string> Errors { get; set; }
    }

    // ---- public wizard ----
    public class ClosedModel
    {
        public DateTime OpensUtc { get; set; }
        public DateTime ClosesUtc { get; set; }
    }

    public class Step1Form : FormBase
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string Company { get; set; }
        public string JobTitle { get; set; }
        public string DietaryRequirements { get; set; }
        public string Notice { get; set; }
    }

    public class TicketOption
    {
        public string Type { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
    }

    public class Step2Form : FormBase
    {
        public string TicketType { get; set; }
        public List<TicketOption> Options { get; set; }
    }

    public class TalkOption
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Speaker { get; set; }
        public string Track { get; set; }
        public DateTime StartsUtc { get; set; }
        public int DurationMinutes { get; set; }
        public string Room { get; set; }
        public int Capacity { get; set; }
        public int Remaining { get; set; }
        public bool Selected { get; set; }
        public bool Full { get; set; }
    }

    public class Step3Form : FormBase
    {
        public int MaxTalks { get; set; }
        public List<TalkOption> Talks { get; set; }
    }

    public class Step4Form : FormBase
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public string TicketType { get; set; }
        public decimal TotalAmount { get; set; }
        public List<string> TalkTitles { get; set; }
        public int SecondsRemaining { get; set; }
        public string ReferenceCode { get; set; }
    }

    public class ConfirmationModel
    {
        public string ReferenceCode { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string TicketType { get; set; }
        public string Status { get; set; }
        public decimal TotalAmount { get; set; }
        public List<string> TalkTitles { get; set; }
    }

    // ---- admin ----
    public class LoginForm : FormBase
    {
        public string Email { get; set; }
        public string Password { get; set; }
        public string ReturnUrl { get; set; }
    }

    public class DashboardModel
    {
        public int PaidToday { get; set; }
        public int PaidTotal { get; set; }
        public int PendingNow { get; set; }
        public decimal TotalRevenue { get; set; }
        public List<RevenueLine> RevenueByTicket { get; set; }
        public List<CapacityWarning> CapacityWarnings { get; set; }
    }

    public class RevenueLine
    {
        public string TicketType { get; set; }
        public int Registrations { get; set; }
        public decimal Revenue { get; set; }
    }

    public class CapacityWarning
    {
        public int TalkId { get; set; }
        public string Title { get; set; }
        public int Capacity { get; set; }
        public int Taken { get; set; }
        public int PercentFull { get; set; }
    }

    public class AttendeeRow
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string Company { get; set; }
        public string JobTitle { get; set; }
        public string DietaryRequirements { get; set; }
        public TicketType? Ticket { get; set; }
        public RegistrationStatus? Status { get; set; }
        public string ReferenceCode { get; set; }
    }

    public class AttendeeList
    {
        public string Q { get; set; }
        public string Ticket { get; set; }
        public string Status { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int Total { get; set; }
        public List<AttendeeRow> Rows { get; set; }
    }

    public class TalkRow
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Speaker { get; set; }
        public string Track { get; set; }
        public DateTime StartsUtc { get; set; }
        public int DurationMinutes { get; set; }
        public string Room { get; set; }
        public int Capacity { get; set; }
        public int Taken { get; set; }
    }

    public class TalkForm : FormBase
    {
        public int Id { get; set; }
        [Required, StringLength(200)] public string Title { get; set; }
        [Required, StringLength(150)] public string Speaker { get; set; }
        [Required, StringLength(50)] public string Track { get; set; }
        public DateTime StartsUtc { get; set; }
        [Range(15, 480)] public int DurationMinutes { get; set; }
        [Required, StringLength(50)] public string Room { get; set; }
        [Range(1, 10000)] public int Capacity { get; set; }
    }

    public class RegistrationRow
    {
        public int Id { get; set; }
        public string ReferenceCode { get; set; }
        public string Attendee { get; set; }
        public string Email { get; set; }
        public string TicketType { get; set; }
        public string Status { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime CreatedUtc { get; set; }
    }

    public class RegistrationList
    {
        public string Status { get; set; }
        public List<RegistrationRow> Rows { get; set; }
    }

    public class PaymentRow
    {
        public int Id { get; set; }
        public decimal Amount { get; set; }
        public string Provider { get; set; }
        public string ProviderRef { get; set; }
        public string Status { get; set; }
        public DateTime ProcessedUtc { get; set; }
    }

    public class RegistrationDetail
    {
        public int Id { get; set; }
        public string ReferenceCode { get; set; }
        public string Attendee { get; set; }
        public string Email { get; set; }
        public string Company { get; set; }
        public string TicketType { get; set; }
        public string Status { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime? ConfirmedUtc { get; set; }
        public List<string> TalkTitles { get; set; }
        public List<PaymentRow> Payments { get; set; }
        public bool CanCancel { get; set; }
        public bool CanRefund { get; set; }
        public string Notice { get; set; }
    }

    public class SettingsForm : FormBase
    {
        public decimal PriceStandard { get; set; }
        public decimal PriceWorkshop { get; set; }
        public decimal PriceStudent { get; set; }
        public DateTime OpensUtc { get; set; }
        public DateTime ClosesUtc { get; set; }
        public string Notice { get; set; }
    }
}
