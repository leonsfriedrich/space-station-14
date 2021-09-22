using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.Damage;
using Content.Shared.Sound;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;

namespace Content.Server.Explosion
{


    public sealed class ExplosionSystem : EntitySystem
    {
        // Todo create explosion prototypes.
        // E.g. Fireball (heat), AP (heat+piercing), HE (heat+blunt), dirty (heat+radiation)
        // Each explosion type will need it's own threshold map


        // TODO
        // Make explosion progress in steps?
        // Avioids dumping all of the damage change events into a single tick.

        // TODO remove tag ExplosivePassable

        // Instead of looking for anchored entities and getting explosion tolerance per tile
        // Really a explosion tolerance should be a PROPERTY of that tile that is updated when anchoring or damaging anchored entities.


        // TODO take final tile set. Turn into grid bound. look for foreign grids intersecting or contained within.
        // For those grids, take their boundary tiles, find what tiles they touch, and use the border to initialize an explosion on the foreign tile.
        // issue: if you have a foreign grid in the center of our station. and make that grid a line of reinforced walls.
        // these will NOT block damage to other grids
        // buut I guess thats fine? not attached --> no roof/ceiling --> explosion goes around?

        private static SoundSpecifier _explosionSound = new SoundCollectionSpecifier("explosion");

        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly DamageableSystem _damageableSystem = default!;

        public DamageSpecifier BaseExplosionDamage = new();

        public override void Initialize()
        {
            base.Initialize();

            BaseExplosionDamage.DamageDict = new() { { "Heat", 5 }, { "Blunt", 5 }, { "Piercing", 5 } };
        }


        // add explosion predictor overlay?

        // test going around corners and through walls
        // test different shielding ammounts
        // test enclosed spaces cause directional explosins
        // test that a fully enclosed space ramps up damage untill the weakest link dies
        // test that en enclosed space with two weak points, with one a bit weaker than the other, both break asymetrically
        // test girders //ExplosivePassable do not block explosions


        ///
        ///Damage is determined by distance
        ///Distance is determined by shortest path
        ///Can travel through walls but is "longer"
        ///pathing on 2d grid makes explosions not perfectly circular


        private void PlaySound(EntityCoordinates coords)
        {
            // Apparently sound system does not accept map coordinates!?
            // TODO set distance based on explosion strength?

            SoundSystem.Play( Filter.Broadcast(), _explosionSound.GetSound(), coords);
        }

        /// <summary>
        ///     This flood a grid, exploring neighbours.
        ///     Each tile has some distance metric that determines damage.
        ///     The issue is, how do we flood / get distance.
        ///     straight distance to center doesn't work, then rounding corners etc doesn't properly reduce damage
        ///
        ///
        ///     Fill all neighbours (inc diagonals) makes all explosions blocky
        ///     fill cardinal neighbours makrs all explosions diamond-ey
        ///
        ///     In reality: most explosions will be constrained by walls, and these geometric shapes will not be noticable.
        ///     MAYBE noticable when nuking a station, but at least the edges (when walls start surviving) will still be fussy.
        ///     SO: for computational simplicitly we could use those.
        ///
        ///     But I say NO!
        ///
        ///     Alternative: method of measuring distances: "Double every other diagonal".
        ///     That looks circlish enough.
        /// 
        ///     Other alternative: just take diagonals to be "sqrt(2)" distance metric away
        ///     Yeah that can also work.... but... what if.... sqrt(2) = 1.5....
        ///     and then we just... count all generations as two distances away
        ///
        ///     then we can avoid assosciating each generation with a specific damage number, and just have damage decreased per generation.
        ///
        /// 
        /// </summary>
        /// <param name="epicenter"></param>
        /// <param name="strength"></param>
        public void SpawnExplosion(MapCoordinates epicenter, int strength, int damagePerIteration)
        {
            if (strength == 0)
                return;

            if (!_mapManager.TryFindGridAt(epicenter, out var grid))
                return;

            var epicenterTile = grid.TileIndicesFor(epicenter);
            // PlaySound(epicenterTile.ToEntityCoordinates(grid.Index, _mapManager));



            // The set of all tiles that will be targeted by this explosion.
            // This is used to stop adding the same tile twice if an explosion loops around an obstacle / encounters itself.
            HashSet<Vector2i> encounteredTiles = new() { epicenterTile };

            // A queue of tiles that are receiving damage, but will only let the explosion spread to neighbors after some delay.
            // The delay duration depends on
            Dictionary<int, Dictionary<Vector2i, int>> blockedTiles = new();

            // A sorted list of sets of tiles that will be targeted by explosions.
            List<HashSet<Vector2i>> explodedTiles = new();
            // Each set of tiles receives the same explosion intensity.
            // The order in which the sets appear in the list corresponds to the "effective distance" to the epicenter (walls increase effective distance).

            // The "distance" is related to the list index via: distance = -0.5 +(index/2)



            // Initialize list with some sets. The first three entries are trivial, but make the following for loop
            // logic nicer. Some of these will be filled in during the iteration.
            explodedTiles.Add(new HashSet<Vector2i>());
            explodedTiles.Add(new HashSet<Vector2i> { epicenterTile });
            explodedTiles.Add(new HashSet<Vector2i>());


            var distributedStrength = 0;
            var iteration = 3;// the tile set iteration we are CURRENTLY adding in every loop
            HashSet<Vector2i> newTiles;
            Dictionary<Vector2i, int>? clearedTiles;
            while (strength > distributedStrength)
            {
                // get the iterator that tells us what tiles we want to find the adjacent neighbors of. usually this is just
                // explodedTiles[index], but it's possible a wall was destroyed and we want to start adding it's
                // neighbors.
                IEnumerable<Vector2i> adjacentIterator = blockedTiles.TryGetValue(iteration - 2, out clearedTiles)
                    ? explodedTiles[iteration - 2].Concat(clearedTiles.Keys)
                    : explodedTiles[iteration - 2];

                // Next, repeat but get the tiles that should explode due to diagonal adjacency
                IEnumerable<Vector2i> diagonalIterator = blockedTiles.TryGetValue(iteration - 3, out clearedTiles)
                    ? explodedTiles[iteration - 3].Concat(clearedTiles.Keys)
                    : explodedTiles[iteration - 3];

                newTiles = GetAdjacentTiles(adjacentIterator, encounteredTiles);
                newTiles.UnionWith(GetDiagonalTiles(diagonalIterator, encounteredTiles));

                // add the new tiles to the list of encountered tiles. this prevents the explosion from looping back on itself
                encounteredTiles.UnionWith(newTiles);

                // check if any of the new tiles are impassable
                var impassableTiles = GetImpassableTiles(newTiles, grid.Index);

                // remove impassable tiles
                newTiles.ExceptWith(impassableTiles.Keys);
                explodedTiles.Add(newTiles);

                // add impassable delays to the set of blocked tiled.
                // these tiles will be added to some future iteration.
                foreach (var (tile, tolerance) in impassableTiles)
                {
                    // How many iterations later would this tile become passable (i.e., when is the wall destroyed and
                    // the explosion can propagate)?

                    var delay = (int) Math.Ceiling((float) tolerance / damagePerIteration);

                    // Add these tiles to some delayed future iteration
                    if (blockedTiles.ContainsKey(iteration + delay))
                        blockedTiles[iteration + delay].Add(tile, iteration);
                    else
                        blockedTiles.Add(iteration + delay, new() { { tile, iteration } });
                }

                iteration += 1;
                distributedStrength += encounteredTiles.Count();
            }
        }

        /// <summary>
        ///     Given a set of tiles, get a list of the ones that are impassable to explosions.
        /// </summary>
        private Dictionary<Vector2i, int> GetImpassableTiles(HashSet<Vector2i> tiles, GridId grid)
        {
            Dictionary<Vector2i, int> impassable = new();
            if (!_gridTileTolerances.TryGetValue(grid, out var tileTolerances))
                return impassable;

            foreach (var tile in tiles)
            {
                if (!tileTolerances.TryGetValue(tile, out var tolerance))
                    continue;

                if (tolerance == 0)
                    continue;

                impassable.Add(tile, tolerance);

            }

            return impassable;
        }

        private Tuple<HashSet<Vector2i>, HashSet<Vector2i>> GetNeighbors(IEnumerable<Vector2i> tiles, HashSet<Vector2i> existingTiles)
        {
            HashSet<Vector2i> adjacentTiles = new();
            HashSet<Vector2i> diagonalTiles = new();
            foreach (var tile in tiles)
            {
                // Hashset question: Is it better to:
                //      A) create a HashSet of tiles, then do ExceptWith after finishing adding all elements
                //      B) only add to a HashSet if the new member is not in the intersection?
                // A) probably has more allocating, but maybe however HashSet intersections are done is inherently faster?
                // So lets use A) for now....
                adjacentTiles.Add(tile + (0, 1));
                adjacentTiles.Add(tile + (1, 0));
                adjacentTiles.Add(tile + (0, -1));
                adjacentTiles.Add(tile + (-1, 0));
                diagonalTiles.Add(tile + (1, 1));
                diagonalTiles.Add(tile + (1, -1));
                diagonalTiles.Add(tile + (-1, 1));
                diagonalTiles.Add(tile + (-1, -1));
            }

            adjacentTiles.ExceptWith(existingTiles);
            diagonalTiles.ExceptWith(existingTiles);
            diagonalTiles.ExceptWith(adjacentTiles);

            return Tuple.Create(adjacentTiles, diagonalTiles);
        }

        private HashSet<Vector2i> GetAdjacentTiles(IEnumerable<Vector2i> tiles, HashSet<Vector2i> existingTiles)
        {
            HashSet<Vector2i> adjacentTiles = new();
            foreach (var tile in tiles)
            {
                // Hashset question: Is it better to:
                //      A) create a HashSet of tiles, then do ExceptWith after finishing adding all elements
                //      B) only add to a HashSet if the new member is not in the intersection?
                // A) probably has more allocating, but maybe however HashSet intersections are done is inherently faster?
                // So lets use A) for now....
                adjacentTiles.Add(tile + (0, 1));
                adjacentTiles.Add(tile + (1, 0));
                adjacentTiles.Add(tile + (0, -1));
                adjacentTiles.Add(tile + (-1, 0));
            }

            adjacentTiles.ExceptWith(existingTiles);
            return adjacentTiles;
        }

        private HashSet<Vector2i> GetDiagonalTiles(IEnumerable<Vector2i> tiles, HashSet<Vector2i> existingTiles)
        {
            HashSet<Vector2i> diagonalTiles = new();
            foreach (var tile in tiles)
            {
                diagonalTiles.Add(tile + (1, 1));
                diagonalTiles.Add(tile + (1, -1));
                diagonalTiles.Add(tile + (-1, 1));
                diagonalTiles.Add(tile + (-1, -1));
            }

            diagonalTiles.ExceptWith(existingTiles);
            return diagonalTiles;
        }
    }
}
