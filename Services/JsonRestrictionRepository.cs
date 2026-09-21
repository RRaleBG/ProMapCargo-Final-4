using System.Text.Json;
using ProMapCargo.Api.Models;
namespace ProMapCargo.Api.Services;
public sealed class JsonRestrictionRepository(IWebHostEnvironment env) : IRestrictionRepository
{
    private readonly Lazy<IReadOnlyList<Restriction>> _data = new(() =>
    {
        var path = Path.Combine(env.ContentRootPath, "Data", "restrictions.json");
        if (!File.Exists(path)) return [];
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<Restriction>>(json,
        new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
    }
    );
    public IReadOnlyList<Restriction> GetAll() => _data.Value;
}
