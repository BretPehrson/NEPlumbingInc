namespace NEPlumbingInc.Services;

public interface IServiceManager
{
    Task<List<ServicesFormModel>> GetAllServicesAsync();
    Task<List<ServicesFormModel>> GetActiveServicesAsync();
    Task<ServicesFormModel> GetServiceByIdAsync(int id);
    Task<ServiceImageData?> GetServiceImageAsync(int id);
    Task<ServicesFormModel> CreateServiceAsync(ServicesFormModel service);
    Task<ServicesFormModel> UpdateServiceAsync(ServicesFormModel service);
    Task DeleteServiceAsync(int id);
}

public sealed record ServiceImageData(byte[] Bytes, string ContentType);

public class ServiceManager(IDbContextFactory<AppDbContext> contextFactory) : IServiceManager
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory = contextFactory;

    public async Task<List<ServicesFormModel>> GetAllServicesAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Services
            .Include(s => s.SubServices)
            .Select(s => new ServicesFormModel
            {
                Id = s.Id,
                ServiceName = s.ServiceName,
                ServiceDescription = s.ServiceDescription,
                HasServiceImage = s.ServiceImage != null,
                IsActive = s.IsActive,
                CreatedAt = s.CreatedAt,
                ConsultationType = s.ConsultationType,
                SubServices = s.SubServices!
                    .Select(sub => new SubServiceModel
                    {
                        Id = sub.Id,
                        Name = sub.Name,
                        Description = sub.Description,
                        Price = sub.Price,
                        ServiceId = sub.ServiceId
                    })
                    .ToList()
            })
            .ToListAsync();
    }

    public async Task<List<ServicesFormModel>> GetActiveServicesAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Services
            .Where(s => s.IsActive)
            .Include(s => s.SubServices)
            .Select(s => new ServicesFormModel
            {
                Id = s.Id,
                ServiceName = s.ServiceName,
                ServiceDescription = s.ServiceDescription,
                HasServiceImage = s.ServiceImage != null,
                IsActive = s.IsActive,
                CreatedAt = s.CreatedAt,
                ConsultationType = s.ConsultationType,
                SubServices = s.SubServices!
                    .Select(sub => new SubServiceModel
                    {
                        Id = sub.Id,
                        Name = sub.Name,
                        Description = sub.Description,
                        Price = sub.Price,
                        ServiceId = sub.ServiceId
                    })
                    .ToList()
            })
            .ToListAsync();
    }

    public async Task<ServicesFormModel> GetServiceByIdAsync(int id)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var service = await context.Services
            .Include(s => s.SubServices)
            .FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new KeyNotFoundException($"Service with ID {id} not found.");

        service.HasServiceImage = service.ServiceImage != null;
        return service;
    }

    public async Task<ServiceImageData?> GetServiceImageAsync(int id)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var dataUri = await context.Services
            .Where(s => s.Id == id)
            .Select(s => s.ServiceImage)
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(dataUri))
        {
            return null;
        }

        const string dataPrefix = "data:";
        const string base64Marker = ";base64,";

        if (!dataUri.StartsWith(dataPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var markerIndex = dataUri.IndexOf(base64Marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var mimeType = dataUri[dataPrefix.Length..markerIndex].Trim();
        if (string.IsNullOrWhiteSpace(mimeType))
        {
            mimeType = "image/jpeg";
        }

        var base64Payload = dataUri[(markerIndex + base64Marker.Length)..];

        try
        {
            var bytes = Convert.FromBase64String(base64Payload);
            return new ServiceImageData(bytes, mimeType);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public async Task<ServicesFormModel> CreateServiceAsync(ServicesFormModel model)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var service = new ServicesFormModel
        {
            ServiceName = model.ServiceName,
            ServiceDescription = model.ServiceDescription,
            ServiceImage = model.ServiceImage,
            IsActive = model.IsActive,
            CreatedAt = DateTime.UtcNow,
            SubServices = model.SubServices ?? new List<SubServiceModel>()
        };

        context.Services.Add(service);
        await context.SaveChangesAsync();
        return service;
    }

    public async Task<ServicesFormModel> UpdateServiceAsync(ServicesFormModel model)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var service = await context.Services
            .Include(s => s.SubServices)
            .FirstOrDefaultAsync(s => s.Id == model.Id);

        if (service == null)
            throw new KeyNotFoundException($"Service {model.Id} not found");

        // Update main service properties
        service.ServiceName = model.ServiceName;
        service.ServiceDescription = model.ServiceDescription;
        service.ServiceImage = model.ServiceImage;
        service.IsActive = model.IsActive;
        service.ConsultationType = model.ConsultationType;  // Add this line

        // Remove deleted sub-services
        var existingIds = service.SubServices!.Select(s => s.Id).ToList();
        var updatedIds = model.SubServices?.Select(s => s.Id).ToList() ?? new List<int>();
        var toRemove = service.SubServices!.Where(s => !updatedIds.Contains(s.Id)).ToList();
        
        foreach (var subService in toRemove)
        {
            context.SubServices.Remove(subService);
        }
    
        // Update existing and add new sub-services
        if (model.SubServices != null)
        {
            foreach (var subService in model.SubServices)
            {
                if (subService.Id == 0)
                {
                    // New sub-service
                    service.SubServices!.Add(new SubServiceModel
                    {
                        Name = subService.Name,
                        Description = subService.Description,
                        Price = subService.Price
                    });
                }
                else
                {
                    // Update existing sub-service
                    var existing = service.SubServices!.FirstOrDefault(s => s.Id == subService.Id);
                    if (existing != null)
                    {
                        existing.Name = subService.Name;
                        existing.Description = subService.Description;
                        existing.Price = subService.Price;
                    }
                }
            }
        }
    
        await context.SaveChangesAsync();
        return service;
    }

    public async Task DeleteServiceAsync(int id)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var service = await context.Services.FindAsync(id)
            ?? throw new KeyNotFoundException($"Service with ID {id} not found.");

        context.Services.Remove(service);
        await context.SaveChangesAsync();
    }
}