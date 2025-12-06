using System.ComponentModel.DataAnnotations;
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Email.Server.Models;

public enum TenantRole
{
    Owner,
    Admin,
    Viewer
}

public class TenantMembers
{
    [ForeignKey("Tenant")]
    public Guid TenantId { get; set; }

    /// <summary>
    /// The user's ID from Entra External ID (Object ID / oid claim)
    /// This is not a FK to AspNetUsers since users exist in Entra
    /// </summary>
    [MaxLength(450)]
    public required string UserId { get; set; }

    /// <summary>
    /// User's email address (cached from Entra for display purposes)
    /// </summary>
    [MaxLength(256)]
    public string? UserEmail { get; set; }

    /// <summary>
    /// User's display name (cached from Entra for display purposes)
    /// </summary>
    [MaxLength(256)]
    public string? UserDisplayName { get; set; }

    [MaxLength(50)]
    public TenantRole TenantRole { get; set; } = TenantRole.Viewer;

    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Tenants? Tenant { get; set; }
}
