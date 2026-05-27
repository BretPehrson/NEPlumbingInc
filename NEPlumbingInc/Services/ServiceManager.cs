using Microsoft.Extensions.Caching.Memory;

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

public class ServiceManager(IDbContextFactory<AppDbContext> contextFactory, IMemoryCache cache) : IServiceManager
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory = contextFactory;
    private readonly IMemoryCache _cache = cache;

    public async Task<List<ServicesFormModel>> GetAllServicesAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var services = await context.Services
            .OrderBy(s => s.Id)
            .Select(s => new ServicesFormModel
            {
                Id = s.Id,
                ServiceName = s.ServiceName,
                ServiceDescription = s.ServiceDescription,
                HasServiceImage = s.ServiceImage != null && s.ServiceImage.StartsWith("data:image/"),
                IsActive = s.IsActive,
                CreatedAt = s.CreatedAt,
                ConsultationType = s.ConsultationType,
                SubServices = new List<SubServiceModel>()
            })
            .ToListAsync();

        if (services.Count == 0)
        {
            return services;
        }

        var serviceIds = services.Select(s => s.Id).ToList();
        var subServices = await context.SubServices
            .Where(sub => serviceIds.Contains(sub.ServiceId))
            .OrderBy(sub => sub.Id)
            .Select(sub => new SubServiceModel
            {
                Id = sub.Id,
                Name = sub.Name,
                Description = sub.Description,
                Price = sub.Price,
                ServiceId = sub.ServiceId
            })
            .ToListAsync();

        var subServicesByServiceId = subServices
            .GroupBy(sub => sub.ServiceId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var service in services)
        {
            service.SubServices = subServicesByServiceId.TryGetValue(service.Id, out var list)
                ? list
                : new List<SubServiceModel>();
        }

        return services;
    }

    public async Task<List<ServicesFormModel>> GetActiveServicesAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var services = await context.Services
            .Where(s => s.IsActive)
            .OrderBy(s => s.Id)
            .Select(s => new ServicesFormModel
            {
                Id = s.Id,
                ServiceName = s.ServiceName,
                ServiceDescription = s.ServiceDescription,
                HasServiceImage = s.ServiceImage != null && s.ServiceImage.StartsWith("data:image/"),
                IsActive = s.IsActive,
                CreatedAt = s.CreatedAt,
                ConsultationType = s.ConsultationType,
                SubServices = new List<SubServiceModel>()
            })
            .ToListAsync();

        if (services.Count == 0)
        {
            return services;
        }

        var serviceIds = services.Select(s => s.Id).ToList();
        var subServices = await context.SubServices
            .Where(sub => serviceIds.Contains(sub.ServiceId))
            .OrderBy(sub => sub.Id)
            .Select(sub => new SubServiceModel
            {
                Id = sub.Id,
                Name = sub.Name,
                Description = sub.Description,
                Price = sub.Price,
                ServiceId = sub.ServiceId
            })
            .ToListAsync();

        var subServicesByServiceId = subServices
            .GroupBy(sub => sub.ServiceId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var service in services)
        {
            service.SubServices = subServicesByServiceId.TryGetValue(service.Id, out var list)
                ? list
                : new List<SubServiceModel>();
        }

        return services;
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
        var cacheKey = $"service-image:{id}";
        if (_cache.TryGetValue<ServiceImageData>(cacheKey, out var cached))
        {
            return cached;
        }

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
        base64Payload = string.Concat(base64Payload.Where(c => !char.IsWhiteSpace(c))).Replace(" ", "+");

        try
        {
            var bytes = Convert.FromBase64String(base64Payload);
            var imageData = new ServiceImageData(bytes, mimeType);

            _cache.Set(
                cacheKey,
                imageData,
                new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromHours(2),
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(8)
                });

            return imageData;
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
        _cache.Remove($"service-image:{service.Id}");

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
        _cache.Remove($"service-image:{id}");
    }
}