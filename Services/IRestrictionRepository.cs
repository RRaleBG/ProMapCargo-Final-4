using ProMapCargo.Api.Models;
namespace ProMapCargo.Api.Services;
public interface IRestrictionRepository
{
    IReadOnlyList<Restriction> GetAll();
}
