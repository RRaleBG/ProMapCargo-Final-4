using ProMapCargo.OsmImporter;
if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: dotnet run --project Importer -- <file.osm.pbf> [graphVersion]");
    return;
}
var pbfPath = Path.GetFullPath(args[0]);
if (!File.Exists(pbfPath))
{
    Console.Error.WriteLine($"PBF not found: {pbfPath}");
    return;
}
var graphVersion = args.Length > 1 && long.TryParse(args[1], out var parsedVersion)
? parsedVersion
: DateTimeOffset.UtcNow.ToUnixTimeSeconds();
var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
?? Environment.GetEnvironmentVariable("PROMAP_POSTGRES")
?? "Host=Host=postgres;Port=5432;Database=promapcargo;Username=promap;Password=promap_dev_change_me";

await new GraphImporter(connectionString).ImportAsync(
pbfPath,
graphVersion,
CancellationToken.None);
Console.WriteLine($"Graph import finished: version {graphVersion}.");
