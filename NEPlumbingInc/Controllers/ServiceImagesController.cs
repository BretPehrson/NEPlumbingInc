using Microsoft.AspNetCore.Mvc;
using NEPlumbingInc.Services;

namespace NEPlumbingInc.Controllers;

[ApiController]
[Route("api/services")]
public class ServiceImagesController(IServiceManager serviceManager) : ControllerBase
{
    private readonly IServiceManager _serviceManager = serviceManager;

    [HttpGet("{id:int}/image")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetImage(int id)
    {
        var image = await _serviceManager.GetServiceImageAsync(id);
        if (image is null)
        {
            return NotFound();
        }

        return File(image.Bytes, image.ContentType);
    }
}