using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._VG;

/// <summary>Small rooms for vg tests to happen in.</summary>
public static class VgTestRooms
{
    /// <summary>
    ///     A walled corridor of four floor tiles, (0, 0) to (3, 0), full of ordinary air.
    /// </summary>
    public static async Task<(Content.IntegrationTests.Pair.TestPair Pair, EntityUid Grid)> AirRoom()
    {
        var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;
        var plating = new Tile(server.ResolveDependency<Robust.Shared.Map.ITileDefinitionManager>()["Plating"].TileId);
        await server.WaitPost(() =>
        {
            var mapSys = server.System<SharedMapSystem>();
            for (var x = -1; x <= 4; x++)
            for (var y = -1; y <= 1; y++)
            {
                mapSys.SetTile(grid, map.Grid.Comp, new Vector2i(x, y), plating);
                if (x is -1 or 4 || y != 0)
                    server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, x + 0.5f, y + 0.5f));
            }
        });
        // Let atmos see the walls before filling the room.
        await server.WaitRunTicks(5);
        await server.WaitPost(() =>
        {
            var atmos = server.System<AtmosphereSystem>();
            for (var x = 0; x <= 3; x++)
            {
                var air = atmos.GetTileMixture(grid, null, new Vector2i(x, 0), true)!;
                air.Clear();
                air.AdjustMoles(Gas.Oxygen, 21.824779f);
                air.AdjustMoles(Gas.Nitrogen, 82.10312f);
                air.Temperature = Atmospherics.T20C;
            }
        });
        return (pair, grid);
    }
}
