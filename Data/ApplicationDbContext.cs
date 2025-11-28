using Email.Server.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Email.Server.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
    }
    // Tenants & membership
    public DbSet<Tenants> Tenants { get; set; }
    public DbSet<TenantMembers> TenantMembers { get; set; }

    // Regions
    public DbSet<RegionsCatalog> RegionsCatalog { get; set; }
    public DbSet<SesRegions> SesRegions { get; set; }
    public DbSet<ConfigSets> ConfigSets { get; set; }

    // Domains & DNS
    public DbSet<Domains> Domains { get; set; }
    public DbSet<DomainDnsRecords> DomainDnsRecords { get; set; }
    public DbSet<Senders> Senders { get; set; }

    // API Keys
    public DbSet<ApiKeys> ApiKeys { get; set; }

    // Messages
    public DbSet<Messages> Messages { get; set; }
    public DbSet<MessageRecipients> MessageRecipients { get; set; }
    public DbSet<MessageTags> MessageTags { get; set; }

    // Events & Analytics
    public DbSet<MessageEvents> MessageEvents { get; set; }

    // Suppressions
    public DbSet<Suppressions> Suppressions { get; set; }

    // Templates & Webhooks
    public DbSet<Templates> Templates { get; set; }
    public DbSet<WebhookEndpoints> WebhookEndpoints { get; set; }
    public DbSet<WebhookDeliveries> WebhookDeliveries { get; set; }

    // Inbound
    public DbSet<InboundMessages> InboundMessages { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Tenants
        builder.Entity<Tenants>(entity =>
        {
            entity.ToTable("Tenants");
            entity.Property(t => t.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        });

        // TenantMembers
        builder.Entity<TenantMembers>(entity =>
        {
            entity.ToTable("TenantMembers");
            entity.HasKey(tm => new { tm.TenantId, tm.UserId });
            entity.HasIndex(tm => tm.UserId).HasDatabaseName("IX_TenantMembers_User");

            entity.HasOne(tm => tm.Tenant)
                .WithMany()
                .HasForeignKey(tm => tm.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(tm => tm.User)
                .WithMany()
                .HasForeignKey(tm => tm.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(tm => tm.TenantRole)
                .HasConversion<string>()
                .HasMaxLength(50);
        });

        // RegionsCatalog
        builder.Entity<RegionsCatalog>(entity =>
        {
            entity.ToTable("RegionsCatalog");
            entity.HasKey(r => r.Region);

            // Seed data
            entity.HasData(
                new RegionsCatalog { Region = "us-east-1", DisplayName = "US East (N. Virginia)", SendSupported = true, ReceiveSupported = true, DefaultForNewTenants = false },
                new RegionsCatalog { Region = "us-west-2", DisplayName = "US West (Oregon)", SendSupported = true, ReceiveSupported = true, DefaultForNewTenants = true },
                new RegionsCatalog { Region = "eu-west-1", DisplayName = "EU (Ireland)", SendSupported = true, ReceiveSupported = true, DefaultForNewTenants = false },
                new RegionsCatalog { Region = "ap-southeast-2", DisplayName = "APAC (Sydney)", SendSupported = true, ReceiveSupported = true, DefaultForNewTenants = false }
            );
        });

        // SesRegions
        builder.Entity<SesRegions>(entity =>
        {
            entity.ToTable("SesRegions");
            entity.Property(s => s.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.HasIndex(s => new { s.TenantId, s.Region }).IsUnique().HasDatabaseName("UQ_SesRegions_TenantRegion");

            entity.HasOne(s => s.Tenant)
                .WithMany()
                .HasForeignKey(s => s.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(s => s.RegionCatalog)
                .WithMany()
                .HasForeignKey(s => s.Region)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // ConfigSets
        builder.Entity<ConfigSets>(entity =>
        {
            entity.ToTable("ConfigSets");
            entity.Property(c => c.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.HasIndex(c => new { c.SesRegionId, c.Name }).IsUnique().HasDatabaseName("UQ_ConfigSets_Scope");

            entity.HasOne(c => c.SesRegion)
                .WithMany()
                .HasForeignKey(c => c.SesRegionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Domains
        builder.Entity<Domains>(entity =>
        {
            entity.ToTable("Domains");
            entity.Property(d => d.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.HasIndex(d => new { d.TenantId, d.Region, d.Domain }).IsUnique().HasDatabaseName("UQ_Domains_TenantRegion");
            entity.HasIndex(d => new { d.TenantId, d.VerificationStatus, d.DkimStatus }).HasDatabaseName("IX_Domains_Tenant_Status");

            entity.HasOne(d => d.Tenant)
                .WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.RegionCatalog)
                .WithMany()
                .HasForeignKey(d => d.Region)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // DomainDnsRecords
        builder.Entity<DomainDnsRecords>(entity =>
        {
            entity.ToTable("DomainDnsRecords");
            entity.HasIndex(d => new { d.DomainId, d.RecordType, d.Host }).IsUnique().HasDatabaseName("UQ_DomainDnsRecords");

            entity.HasOne(d => d.Domain)
                .WithMany()
                .HasForeignKey(d => d.DomainId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Senders
        builder.Entity<Senders>(entity =>
        {
            entity.ToTable("Senders");
            entity.Property(s => s.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.HasIndex(s => new { s.DomainId, s.Email }).IsUnique().HasDatabaseName("UQ_Senders");

            entity.HasOne(s => s.Domain)
                .WithMany()
                .HasForeignKey(s => s.DomainId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ApiKeys
        builder.Entity<ApiKeys>(entity =>
        {
            entity.ToTable("ApiKeys");
            entity.Property(a => a.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.HasIndex(a => a.TenantId).HasDatabaseName("IX_ApiKeys_Tenant");
            entity.HasIndex(a => a.KeyPrefix).IsUnique().HasDatabaseName("UX_ApiKeys_Prefix");
            entity.HasIndex(a => a.DomainId).HasDatabaseName("IX_ApiKeys_Domain");

            entity.HasOne(a => a.Tenant)
                .WithMany()
                .HasForeignKey(a => a.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.Domain)
                .WithMany()
                .HasForeignKey(a => a.DomainId)
                .OnDelete(DeleteBehavior.NoAction); // NoAction to avoid cascade cycle with Tenant
        });

        // Messages
        builder.Entity<Messages>(entity =>
        {
            entity.ToTable("Messages");
            entity.Property(m => m.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.HasIndex(m => new { m.TenantId, m.RequestedAtUtc }).HasDatabaseName("IX_Messages_Tenant_Time");
            entity.HasIndex(m => m.SesMessageId).HasDatabaseName("IX_Messages_SesMessageId");

            entity.HasOne(m => m.Tenant)
                .WithMany()
                .HasForeignKey(m => m.TenantId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(m => m.RegionCatalog)
                .WithMany()
                .HasForeignKey(m => m.Region)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(m => m.ConfigSet)
                .WithMany()
                .HasForeignKey(m => m.ConfigSetId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // MessageRecipients
        builder.Entity<MessageRecipients>(entity =>
        {
            entity.ToTable("MessageRecipients");
            entity.HasIndex(m => m.MessageId).HasDatabaseName("IX_MessageRecipients_Message");
            entity.HasIndex(m => m.DeliveryStatus).HasDatabaseName("IX_MessageRecipients_Status");

            entity.HasOne(m => m.Message)
                .WithMany()
                .HasForeignKey(m => m.MessageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // MessageTags
        builder.Entity<MessageTags>(entity =>
        {
            entity.ToTable("MessageTags");
            entity.HasKey(mt => new { mt.MessageId, mt.Name });

            entity.HasOne(mt => mt.Message)
                .WithMany()
                .HasForeignKey(mt => mt.MessageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // MessageEvents
        builder.Entity<MessageEvents>(entity =>
        {
            entity.ToTable("MessageEvents");
            entity.HasIndex(me => new { me.TenantId, me.OccurredAtUtc }).HasDatabaseName("IX_MessageEvents_Tenant_Time");

            entity.HasOne(me => me.Message)
                .WithMany()
                .HasForeignKey(me => me.MessageId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(me => me.Tenant)
                .WithMany()
                .HasForeignKey(me => me.TenantId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(me => me.RegionCatalog)
                .WithMany()
                .HasForeignKey(me => me.Region)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // Suppressions
        builder.Entity<Suppressions>(entity =>
        {
            entity.ToTable("Suppressions");
            entity.HasIndex(s => new { s.TenantId, s.Region, s.Email }).IsUnique().HasDatabaseName("UQ_Suppressions")
                .HasFilter("[Region] IS NOT NULL");
            entity.HasIndex(s => new { s.TenantId, s.Email }).HasDatabaseName("IX_Suppressions_Tenant_Email");

            entity.HasOne(s => s.Tenant)
                .WithMany()
                .HasForeignKey(s => s.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Templates
        builder.Entity<Templates>(entity =>
        {
            entity.ToTable("Templates");
            entity.Property(t => t.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.HasIndex(t => new { t.TenantId, t.Name, t.Version }).IsUnique().HasDatabaseName("UQ_Templates");

            entity.HasOne(t => t.Tenant)
                .WithMany()
                .HasForeignKey(t => t.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // WebhookEndpoints
        builder.Entity<WebhookEndpoints>(entity =>
        {
            entity.ToTable("WebhookEndpoints");
            entity.Property(w => w.Id).HasDefaultValueSql("NEWSEQUENTIALID()");

            entity.HasOne(w => w.Tenant)
                .WithMany()
                .HasForeignKey(w => w.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // WebhookDeliveries
        builder.Entity<WebhookDeliveries>(entity =>
        {
            entity.ToTable("WebhookDeliveries");
            entity.HasIndex(w => new { w.EndpointId, w.Status }).HasDatabaseName("IX_WebhookDeliveries_Endpoint_Status");

            entity.HasOne(w => w.Endpoint)
                .WithMany()
                .HasForeignKey(w => w.EndpointId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(w => w.Event)
                .WithMany()
                .HasForeignKey(w => w.EventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // InboundMessages
        builder.Entity<InboundMessages>(entity =>
        {
            entity.ToTable("InboundMessages");
            entity.Property(i => i.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.HasIndex(i => new { i.TenantId, i.ReceivedAtUtc }).HasDatabaseName("IX_InboundMessages_Tenant_Time");

            entity.HasOne(i => i.Tenant)
                .WithMany()
                .HasForeignKey(i => i.TenantId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(i => i.RegionCatalog)
                .WithMany()
                .HasForeignKey(i => i.Region)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
