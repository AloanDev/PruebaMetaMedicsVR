using UnityEngine;
using System.Collections.Generic;

public class GeneratedChunkData
{
    public Vector3 worldPosition;
    public int chunkSeed;
    public OrganicChunkGenerator.ConnectionDirection entryDirection;
    public List<OrganicChunkGenerator.ConnectionDirection> activeExitDirections = new List<OrganicChunkGenerator.ConnectionDirection>();
    public int levelChunkIndex; // Índice único del chunk en el nivel

    // Puedes añadir más datos aquí si el chunk guarda estado de interactuables, enemigos, etc.
    // Por ahora, solo necesitamos lo esencial para regenerar su forma.

    public GeneratedChunkData(Vector3 pos, int seed, OrganicChunkGenerator.ConnectionDirection entryDir, int index)
    {
        worldPosition = pos;
        chunkSeed = seed;
        entryDirection = entryDir;
        levelChunkIndex = index;
    }
}