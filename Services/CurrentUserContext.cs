using System.Security.Claims;
namespace ProMapCargo.Api.Services;
public interface ICurrentUserContext {
    Guid? UserId{get;}Guid? CompanyId{get;}bool IsAuthenticated{get;}bool IsAdministrator{get;}bool IsInRole(string role);
}
public sealed class CurrentUserContext(IHttpContextAccessor a):ICurrentUserContext {
    ClaimsPrincipal U=>a.HttpContext?.User??new();
    public Guid? UserId=>Guid.TryParse(U.FindFirstValue(ClaimTypes.NameIdentifier),out var x)?x:null;
    public Guid? CompanyId=>Guid.TryParse(U.FindFirstValue("company_id"),out var x)?x:null;
    public bool IsAuthenticated=>U.Identity?.IsAuthenticated==true;
    public bool IsAdministrator=>U.IsInRole("Administrator");
    public bool IsInRole(string role)=>U.IsInRole(role);
}
