using UnityEngine;
using System.Collections.Generic;

public class GeneratedChunkData
{
    public Vector3 worldPosition;
    public int chunkSeed;
    public OrganicChunkGenerator.ConnectionDirection entryDirection;
    public List<OrganicChunkGenerator.ConnectionDirection> activeExitDirections = new List<OrganicChunkGenerator.ConnectionDirection>();
    public int levelChunkIndex; // Índice único del chunk en el nivel

    // --- NUEVO: Datos de altura del terreno generados por Perlin noise ---
    public int[,] terrainHeights; // Almacenará la altura (Y) de cada bloque (X, Z)
    // -------------------------------------------------------------------

    public GeneratedChunkData(Vector3 pos, int seed, OrganicChunkGenerator.ConnectionDirection entryDir, int index)
    {
        worldPosition = pos;
        chunkSeed = seed;
        entryDirection = entryDir;
        levelChunkIndex = index;
    }
}