using System.ComponentModel.DataAnnotations.Schema;

namespace NEPlumbingInc.Models;

public enum ConsultationType
{
    Inspection,
    Consultation
}

public class ServicesFormModel
{
    public int Id { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string ServiceDescription { get; set; } = string.Empty;
    public string? ServiceImage { get; set; }
    [NotMapped]
    public bool HasServiceImage { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public ConsultationType ConsultationType { get; set; } = ConsultationType.Inspection;
    public List<SubServiceModel>? SubServices { get; set; }
}

public class SubServiceModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal? Price { get; set; }
    public int ServiceId { get; set; }
    public ServicesFormModel? Service { get; set; }
}