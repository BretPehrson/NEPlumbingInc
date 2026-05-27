using Microsoft.AspNetCore.Mvc;
using NEPlumbingInc.Services;

namespace NEPlumbingInc.Controllers;

[ApiController]
[Route("api/services")]
public class ServiceImagesController(IServiceManager serviceManager) : ControllerBase
{
    private readonly IServiceManager _serviceManager = serviceManager;
    private static readonly byte[] TransparentGifPixel = Convert.FromBase64String("R0lGODlhAQABAIABAP///wAAACwAAAAAAQABAAACAkQBADs=");

    [HttpGet("{id:int}/image")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetImage(int id)
    {
        var image = await _serviceManager.GetServiceImageAsync(id);
        if (image is null)
        {
            return File(TransparentGifPixel, "image/gif");
        }

        return File(image.Bytes, image.ContentType);
    }
}