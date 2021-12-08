using Content.Shared.Atmos;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Serialization;
using System;
using System.Collections.Generic;

namespace Content.Shared.Explosion;

// Temporary file for testing multi-grid explosions
// TODO EXPLOSIONS REMOVE
[Serializable, NetSerializable]
public class GridEdgeUpdateEvent : EntityEventArgs
{
    public GridId Reference;
    public Dictionary<GridId, Dictionary<Vector2i, AtmosDirection>> GridEdges;
    public Dictionary<GridId, HashSet<Vector2i>> DiagGridEdges;

    public GridEdgeUpdateEvent(GridId reference,
        Dictionary<GridId, Dictionary<Vector2i, AtmosDirection>> gridEdges,
        Dictionary<GridId, HashSet<Vector2i>> diagGridEdges)
    {
        Reference = reference;
        GridEdges = gridEdges;
        DiagGridEdges = diagGridEdges;
    }
}

/// <summary>
///     An explosion event. Used for client side rendering.
/// </summary>
[Serializable, NetSerializable]
public class ExplosionEvent : EntityEventArgs
{
    public MapCoordinates Epicenter;

    public Dictionary<GridId, Dictionary<int, HashSet<Vector2i>>> Tiles;

    public List<float> Intensity;

    public string TypeID;

    public Matrix3 SpaceMatrix;

    public ExplosionEvent(MapCoordinates epicenter, string typeID, List<float> intensity, Dictionary<GridId, Dictionary<int, HashSet<Vector2i>>> tiles, Matrix3 spaceMatrix)
    {
        Epicenter = epicenter;
        Tiles = tiles;
        Intensity = intensity;
        TypeID = typeID;
        SpaceMatrix = spaceMatrix;
    }
}

/// <summary>
///     Update visual rendering of the explosion to correspond to the servers processing of it.
/// </summary>
[Serializable, NetSerializable]
public class ExplosionOverlayUpdateEvent : EntityEventArgs
{
    public int Index;

    public ExplosionOverlayUpdateEvent(int index)
    {
        Index = index;
    }
}
