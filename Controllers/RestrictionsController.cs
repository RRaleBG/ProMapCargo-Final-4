using Microsoft.AspNetCore.Mvc;
using ProMapCargo.Api.Services;
namespace ProMapCargo.Api.Controllers;
[ApiController]
[Route("api/restrictions")]
public sealed class RestrictionsController(IRestrictionRepository repository) : ControllerBase
{
    [HttpGet]
    public IActionResult GetAll() => Ok(repository.GetAll());
}
