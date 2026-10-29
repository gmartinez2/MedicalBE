namespace Medical.Domain.Common;

/// <summary>Base type for entities that carry an integer key and audit columns.</summary>
public abstract class AuditableEntity
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
