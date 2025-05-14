using DysonNetwork.Sphere.Publisher;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Sphere.Developer;

[ApiController]
[Route("/developers/apps")]
public class CustomAppController(PublisherService ps) : ControllerBase
{
    
}