using System.Runtime.CompilerServices;
using Content.Server.Atmos.Components;
using Content.Server.Maps;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.Atmos.EntitySystems;

public partial class AtmosphereSystem
{
    /// <summary>
    /// Gets the particular price of an air mixture.
    /// </summary>
    public double GetPrice(GasMixture mixture)
    {
        float basePrice = 0; // moles of gas * price/mole
        float totalMoles = 0; // total number of moles in can
        float maxComponent = 0; // moles of the dominant gas
        for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
        {
            basePrice += mixture.Moles[i] * GetGas(i).PricePerMole;
            totalMoles += mixture.Moles[i];
            maxComponent = Math.Max(maxComponent, mixture.Moles[i]);
        }

        // Pay more for gas canisters that are more pure
        float purity = 1;
        if (totalMoles > 0) {
            purity = maxComponent / totalMoles;
        }

        return basePrice * purity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void InvalidateVisuals(EntityUid gridUid, Vector2i tile, GasTileOverlayComponent? comp = null)
    {
        _gasTileOverlaySystem.Invalidate(gridUid, tile, comp);
    }

    /// <summary>
    ///     Gets the volume in liters for a number of tiles, on a specific grid.
    /// </summary>
    /// <param name="mapGrid">The grid in question.</param>
    /// <param name="tiles">The amount of tiles.</param>
    /// <returns>The volume in liters that the tiles occupy.</returns>
    private float GetVolumeForTiles(MapGridComponent mapGrid, int tiles = 1)
    {
        return Atmospherics.CellVolume * mapGrid.TileSize * tiles;
    }

    private (AtmosDirection Blocked, bool NoAir, bool FixVacuum) GetAirtightData(Entity<MapGridComponent> grid, Vector2i indices)
    {
        var directions = AtmosDirection.Invalid;
        var noAir = false;
        var fixVacuum = false;

        foreach (var ent in _map.GetAnchoredEntities(grid.Owner, grid.Comp, indices))
        {
            if (!_airtightQuery.TryGetComponent(ent, out var airtight))
                continue;

            if(!airtight.AirBlocked)
                continue;

            directions |= airtight.AirBlockedDirection;
            noAir |= airtight.NoAirWhenFullyAirBlocked;
            fixVacuum |= airtight.FixVacuum;

            if (directions == AtmosDirection.All && noAir && fixVacuum)
                break;
        }

        return (directions, noAir, fixVacuum);
    }

    /// <summary>
    ///     Pries a tile in a grid.
    /// </summary>
    /// <param name="mapGrid">The grid in question.</param>
    /// <param name="tile">The indices of the tile.</param>
    private void PryTile(MapGridComponent mapGrid, Vector2i tile)
    {
        if (!mapGrid.TryGetTileRef(tile, out var tileRef))
            return;

        _tile.PryTile(tileRef);
    }
}
