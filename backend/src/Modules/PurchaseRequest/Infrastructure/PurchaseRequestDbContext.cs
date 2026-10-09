using Acme.Core.Data;
using Acme.PurchaseRequest.Domain;
using Microsoft.EntityFrameworkCore;

namespace Acme.PurchaseRequest.Infrastructure;

/// <summary>The module's data in each tenant's database, in its own schema. No other module reads these tables.</summary>
public sealed class PurchaseRequestDbContext(DbContextOptions<PurchaseRequestDbContext> options) : ModuleDbContext(options)
{
    public DbSet<Domain.PurchaseRequest> Requests => Set<Domain.PurchaseRequest>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Domain.PurchaseRequest>(request =>
        {
            request.ToTable("Requests");
            request.Property(r => r.Number).HasMaxLength(60);
            request.HasIndex(r => r.Number).IsUniqueAmongNotDeleted();
            request.Property(r => r.Title).HasMaxLength(Domain.PurchaseRequest.MaxTitleLength);
            request.Property(r => r.Description).HasMaxLength(Domain.PurchaseRequest.MaxDescriptionLength);
            request.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            request.Property(r => r.Currency).HasMaxLength(3).IsFixedLength();
            request.Property(r => r.RejectionReason).HasMaxLength(500);
            request.Ignore(r => r.Total);
            request.Ignore(r => r.IsEditable);
            request.HasIndex(r => new { r.Status, r.SubmittedOn });
            request.HasIndex(r => new { r.RequesterId, r.CreatedOn });

            request.OwnsMany(r => r.Lines, line =>
            {
                line.ToTable("RequestLines");
                line.WithOwner().HasForeignKey("RequestId");
                line.Property<int>("LineNo");
                line.HasKey("RequestId", "LineNo");
                line.Property(l => l.Item).HasMaxLength(RequestLine.MaxItemLength);
                line.Ignore(l => l.LineTotal);
            });
            request.OwnsMany(r => r.Approvals, approval =>
            {
                approval.ToTable("RequestApprovals");
                approval.WithOwner().HasForeignKey("RequestId");
                approval.HasKey("RequestId", nameof(RequestApproval.Level), nameof(RequestApproval.ApproverId));
            });
            request.Navigation(r => r.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
            request.Navigation(r => r.Approvals).HasField("_approvals").UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }
}
