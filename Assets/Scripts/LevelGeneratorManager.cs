using UnityEngine;
using System.Collections.Generic;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Gestiona la generación dinámica del nivel, el object pooling de chunks,
/// y la activación/desactivación de chunks basada en la posición del jugador.
/// </summary>
public class LevelGeneratorManager : MonoBehaviour
{
    [Header("Generation Settings")]
    [Tooltip("Semilla global para la generación del nivel (0 para aleatorio).")]
    public int globalSeed = 0;

    [Tooltip("Prefab del chunk orgánico (debe tener el script OrganicChunkGenerator).")]
    public GameObject organicChunkPrefab;

    [Tooltip("Dimensión de un solo chunk (ej. 13 para 13x13).")]
    public int chunkDimension = 13;

    [Header("Perlin Noise Settings")]
    [Tooltip("Escala (frecuencia) del ruido Perlin. Valores más bajos = colinas más grandes.")]
    public float noiseScale = 0.1f;

    [Tooltip("Altura máxima que pueden alcanzar las montañas sobre el suelo base (0).")]
    [Range(0, 3)]
    public int maxMountainHeight = 3;

    [Tooltip("El offset de altura base del terreno (la Y mínima de la capa de césped/tierra).")]
    public int baseTerrainHeight = 0;

    [Tooltip("Densidad para la capa de tierra bajo el césped. Más alto = capa de tierra más gruesa.")]
    public int dirtLayerDepth = 3;


    [Header("Level Flow Settings")]
    [Tooltip("Referencia al GameObject del jugador para el seguimiento de la posición. ¡Asignar en el Inspector!")]
    public GameObject playerReference;

    [Tooltip(
        "Número de chunks a mantener activos alrededor del jugador (ej. 3 para el chunk actual y 1 en cada dirección).")]
    public int maxActiveChunksWindow = 3;

    [Tooltip(
        "Frecuencia con la que se intentan crear bifurcaciones (0 para deshabilitar). Ej: 3 significa cada 3 chunks.")]
    public int forkFrequency = 3;

    [Header("Player Start Settings")]
    [Tooltip("Si el jugador debe moverse automáticamente al inicio del primer chunk generado.")]
    public bool movePlayerToStart = true;

    private List<OrganicChunkGenerator> chunkPool = new List<OrganicChunkGenerator>();

    private List<OrganicChunkGenerator> activeChunks = new List<OrganicChunkGenerator>();

    private Dictionary<Vector2Int, GeneratedChunkData> generatedLevelMap =
        new Dictionary<Vector2Int, GeneratedChunkData>();

    private int nextChunkUniqueIndex = 0;

    private Vector2Int currentChunkPlayerIsInGrid = Vector2Int.zero;

    private class ConnectionSlot
    {
        public OrganicChunkGenerator.ConnectionDirection connectionDirection;
        public Vector2Int parentChunkGridPosition;
        public OrganicChunkGenerator parentChunkInstance;

        public ConnectionSlot(OrganicChunkGenerator.ConnectionDirection dir, Vector2Int parentGridPos,
            OrganicChunkGenerator parentInstance)
        {
            connectionDirection = dir;
            parentChunkGridPosition = parentGridPos;
            parentChunkInstance = parentInstance;
        }
    }

    private List<ConnectionSlot> openConnectionSlots = new List<ConnectionSlot>();

    void Start()
    {
        InitializeChunkPool(maxActiveChunksWindow * maxActiveChunksWindow * 2);
        GenerateInitialLevel();
    }

    void Update()
    {
        if (playerReference == null)
        {
            Debug.LogWarning("Player Reference no asignado en LevelGeneratorManager. No se puede actualizar el nivel.");
            return;
        }

        Vector2Int newPlayerChunkGrid = GetChunkGridPositionFromWorldPosition(playerReference.transform.position);

        if (newPlayerChunkGrid != currentChunkPlayerIsInGrid)
        {
            currentChunkPlayerIsInGrid = newPlayerChunkGrid;
            Debug.Log($"Jugador ha entrado al chunk de la cuadrícula: {currentChunkPlayerIsInGrid}");
            UpdateLevelChunks();
        }
    }

    /// <summary>
    /// Inicializa el pool de chunks pre-instanciando y desactivando GameObjects.
    /// </summary>
    /// <param name="poolSize">El número de chunks a pre-instanciar.</param>
    private void InitializeChunkPool(int poolSize)
    {
        for (int i = 0; i < poolSize; i++)
        {
            GameObject chunkGO = Instantiate(organicChunkPrefab, Vector3.zero, Quaternion.identity, transform);
            OrganicChunkGenerator chunk = chunkGO.GetComponent<OrganicChunkGenerator>();
            if (chunk == null)
            {
                Debug.LogError("El prefab de chunk no tiene el script OrganicChunkGenerator.");
                Destroy(chunkGO);
                continue;
            }

            chunkGO.SetActive(false);
            chunkPool.Add(chunk);
        }

        Debug.Log($"Pool de chunks inicializado con {chunkPool.Count} chunks.");
    }

    /// <summary>
    /// Obtiene un chunk del pool (lo recicla) o crea uno nuevo si el pool está vacío.
    /// </summary>
    /// <returns>Una instancia de OrganicChunkGenerator activa y lista para usar.</returns>
    private OrganicChunkGenerator GetChunkFromPool()
    {
        if (chunkPool.Any())
        {
            OrganicChunkGenerator chunk = chunkPool[0];
            chunkPool.RemoveAt(0);
            chunk.gameObject.SetActive(true);
            return chunk;
        }
        else
        {
            Debug.LogWarning("Pool de chunks vacío, instanciando uno nuevo.");
            GameObject chunkGO = Instantiate(organicChunkPrefab, Vector3.zero, Quaternion.identity, transform);
            OrganicChunkGenerator chunk = chunkGO.GetComponent<OrganicChunkGenerator>();
            if (chunk == null)
            {
                Debug.LogError("El prefab de chunk instanciado no tiene el script OrganicChunkGenerator.");
                Destroy(chunkGO);
                return null;
            }

            chunk.gameObject.SetActive(true);
            return chunk;
        }
    }

    /// <summary>
    /// Devuelve un chunk al pool, desactivándolo y haciéndolo disponible para reutilización.
    /// </summary>
    /// <param name="chunk">La instancia del chunk a devolver.</param>
    private void ReturnChunkToPool(OrganicChunkGenerator chunk)
    {
        if (chunk != null && !chunkPool.Contains(chunk))
        {
            chunk.gameObject.SetActive(false);
            chunk.transform.position = Vector3.zero;
            chunkPool.Add(chunk);
            activeChunks.Remove(chunk);
        }
    }

    /// <summary>
    /// Calcula la posición en la cuadrícula (X, Z) de un chunk dada una posición global.
    /// </summary>
    /// <param name="worldPos">Posición mundial.</param>
    /// <returns>La posición en la cuadrícula del chunk.</returns>
    private Vector2Int GetChunkGridPositionFromWorldPosition(Vector3 worldPos)
    {
        return new Vector2Int(
            Mathf.FloorToInt(worldPos.x / chunkDimension),
            Mathf.FloorToInt(worldPos.z / chunkDimension)
        );
    }

    /// <summary>
    /// Calcula la posición mundial de un chunk dado su posición en la cuadrícula.
    /// </summary>
    /// <param name="gridPos">Posición en la cuadrícula (ej. (0,0), (1,0)).</param>
    /// <returns>La posición mundial del origen (esquina inferior-izquierda) del chunk.</returns>
    private Vector3 GetWorldPositionFromGridPosition(Vector2Int gridPos)
    {
        return new Vector3(gridPos.x * chunkDimension, 0, gridPos.y * chunkDimension);
    }

    /// <summary>
    /// Genera el primer chunk del nivel y posiciona al jugador si está configurado.
    /// </summary>
    private void GenerateInitialLevel()
    {
        if (globalSeed == 0)
        {
            globalSeed = Random.Range(int.MinValue, int.MaxValue);
        }

        Random.InitState(globalSeed);
        Debug.Log($"Iniciando generación de nivel con semilla global: {globalSeed}");

        Vector2Int initialGridPos = Vector2Int.zero;
        Vector3 initialWorldPos = GetWorldPositionFromGridPosition(initialGridPos);

        GeneratedChunkData firstChunkData = new GeneratedChunkData(initialWorldPos,
            Random.Range(int.MinValue, int.MaxValue), OrganicChunkGenerator.ConnectionDirection.Center,
            nextChunkUniqueIndex++);
        firstChunkData.activeExitDirections.Add(
            GetRandomValidExitDirection(OrganicChunkGenerator.ConnectionDirection.Center));

        firstChunkData.terrainHeights = GenerateChunkTerrainHeights(initialGridPos.x, initialGridPos.y, chunkDimension);

        GenerateAndActivateChunk(firstChunkData, initialGridPos);
        currentChunkPlayerIsInGrid = initialGridPos;

        if (movePlayerToStart && playerReference != null)
        {
            OrganicChunkGenerator initialChunkInstance = activeChunks.FirstOrDefault(c =>
                GetChunkGridPositionFromWorldPosition(c.transform.position) == initialGridPos);

            if (initialChunkInstance != null)
            {
                Vector2Int localEntryPoint = initialChunkInstance.EntryPoint;
                OrganicChunkGenerator.ConnectionDirection entryDir = initialChunkInstance.EntryDirection;
                Vector2Int localInternalPathStart =
                    initialChunkInstance.GetPathInternalPoint(localEntryPoint, entryDir);

                Vector3 startPosition =
                    new Vector3(localInternalPathStart.x, playerReference.transform.position.y, localInternalPathStart.y) + initialChunkInstance.transform.position;

                playerReference.transform.position = startPosition;
                Debug.Log($"Jugador movido a la posición de inicio del chunk (0,0): {startPosition}");
            }
            else
            {
                Debug.LogWarning("No se encontró la instancia del chunk inicial para mover al jugador.");
            }
        }

        UpdateLevelChunks();
    }

    /// <summary>
    /// Actualiza los chunks activos en el nivel basándose en la posición actual del jugador.
    /// Recicla los chunks fuera de la ventana y genera/reactiva los nuevos.
    /// </summary>
    private void UpdateLevelChunks()
    {
        HashSet<Vector2Int> desiredActiveGridPositions = new HashSet<Vector2Int>();
        desiredActiveGridPositions.Add(currentChunkPlayerIsInGrid);

        for (int x = -(maxActiveChunksWindow / 2); x <= (maxActiveChunksWindow / 2); x++)
        {
            for (int y = -(maxActiveChunksWindow / 2); y <= (maxActiveChunksWindow / 2); y++)
            {
                Vector2Int relativePos = new Vector2Int(x, y);
                if (Mathf.Abs(relativePos.x) + Mathf.Abs(relativePos.y) <= maxActiveChunksWindow / 2)
                {
                    desiredActiveGridPositions.Add(currentChunkPlayerIsInGrid + relativePos);
                }
            }
        }

        List<OrganicChunkGenerator> chunksToDeactivate = new List<OrganicChunkGenerator>();
        foreach (var chunkInstance in activeChunks)
        {
            Vector2Int chunkGridPos = GetChunkGridPositionFromWorldPosition(chunkInstance.transform.position);
            if (!desiredActiveGridPositions.Contains(chunkGridPos))
            {
                chunksToDeactivate.Add(chunkInstance);
            }
        }

        foreach (var chunkInstance in chunksToDeactivate)
        {
            Debug.Log(
                $"Desactivando chunk en {chunkInstance.transform.position} (Grid: {GetChunkGridPositionFromWorldPosition(chunkInstance.transform.position)})");
            ReturnChunkToPool(chunkInstance);
        }

        foreach (Vector2Int gridPos in desiredActiveGridPositions)
        {
            OrganicChunkGenerator activeInstance = activeChunks.FirstOrDefault(c =>
                GetChunkGridPositionFromWorldPosition(c.transform.position) == gridPos);

            if (activeInstance == null)
            {
                if (generatedLevelMap.TryGetValue(gridPos, out GeneratedChunkData chunkData))
                {
                    Debug.Log($"Reactivando chunk existente en Grid: {gridPos} (ID: {chunkData.levelChunkIndex})");
                    GenerateAndActivateChunk(chunkData, gridPos);
                }
                else
                {
                    ConnectionSlot slotToUse = openConnectionSlots.FirstOrDefault(s =>
                        GetTargetGridPosition(s.parentChunkGridPosition, s.connectionDirection) == gridPos);

                    if (slotToUse != null)
                    {
                        List<OrganicChunkGenerator.ConnectionDirection> desiredExits =
                            new List<OrganicChunkGenerator.ConnectionDirection>();
                        OrganicChunkGenerator.ConnectionDirection newChunkEntryDir =
                            GetOppositeDirection(slotToUse.connectionDirection);

                        bool tryFork = (forkFrequency > 0 && nextChunkUniqueIndex % forkFrequency == 0);

                        if (tryFork)
                        {
                            OrganicChunkGenerator.ConnectionDirection mainExit =
                                GetRandomValidExitDirection(newChunkEntryDir);
                            if (mainExit != OrganicChunkGenerator.ConnectionDirection.None)
                            {
                                desiredExits.Add(mainExit);
                            }

                            List<OrganicChunkGenerator.ConnectionDirection> possibleForkDirs =
                                new List<OrganicChunkGenerator.ConnectionDirection>
                                {
                                    OrganicChunkGenerator.ConnectionDirection.North,
                                    OrganicChunkGenerator.ConnectionDirection.South,
                                    OrganicChunkGenerator.ConnectionDirection.East,
                                    OrganicChunkGenerator.ConnectionDirection.West
                                };
                            possibleForkDirs.Remove(newChunkEntryDir);
                            possibleForkDirs.Remove(mainExit);

                            if (possibleForkDirs.Any())
                            {
                                OrganicChunkGenerator.ConnectionDirection forkExit =
                                    possibleForkDirs[Random.Range(0, possibleForkDirs.Count)];
                                desiredExits.Add(forkExit);
                                Debug.Log($"Chunk a generar en {gridPos} tiene bifurcación en {forkExit}");
                            }
                        }

                        if (!desiredExits.Any())
                        {
                            desiredExits.Add(GetRandomValidExitDirection(newChunkEntryDir));
                        }

                        Vector3 newChunkWorldPos = GetWorldPositionFromGridPosition(gridPos);
                        GeneratedChunkData newChunkData = new GeneratedChunkData(newChunkWorldPos,
                            Random.Range(int.MinValue, int.MaxValue), newChunkEntryDir, nextChunkUniqueIndex++);
                        newChunkData.activeExitDirections = desiredExits;

                        newChunkData.terrainHeights = GenerateChunkTerrainHeights(gridPos.x, gridPos.y, chunkDimension);

                        bool chunkPlaced = TryPlaceChunk(newChunkData, gridPos, slotToUse);
                        if (chunkPlaced)
                        {
                        }
                    }
                }
            }
        }

        openConnectionSlots.RemoveAll(slot =>
                !activeChunks.Contains(slot.parentChunkInstance) ||
                generatedLevelMap.ContainsKey(GetTargetGridPosition(slot.parentChunkGridPosition,
                    slot.connectionDirection))
        );
    }

    /// <summary>
    /// Calcula la posición en la cuadrícula a la que llevaría una conexión desde un chunk padre.
    /// </summary>
    /// <param name="parentGridPos">Posición en cuadrícula del chunk padre.</param>
    /// <param name="connectionDir">Dirección de la conexión desde el padre.</param>
    /// <returns>La posición en cuadrícula del chunk destino.</returns>
    private Vector2Int GetTargetGridPosition(Vector2Int parentGridPos,
        OrganicChunkGenerator.ConnectionDirection connectionDir)
    {
        return connectionDir switch
        {
            OrganicChunkGenerator.ConnectionDirection.North => parentGridPos + Vector2Int.up,
            OrganicChunkGenerator.ConnectionDirection.South => parentGridPos + Vector2Int.down,
            OrganicChunkGenerator.ConnectionDirection.East => parentGridPos + Vector2Int.right,
            OrganicChunkGenerator.ConnectionDirection.West => parentGridPos + Vector2Int.left,
            _ => parentGridPos
        };
    }

    /// <summary>
    /// Intenta colocar un chunk en el nivel, lo obtiene del pool, lo activa, lo genera y actualiza el mapa.
    /// </summary>
    /// <param name="chunkData">Los datos de la configuración del chunk a colocar.</param>
    /// <param name="chunkGridPos">La posición en la cuadrícula donde se colocará el chunk.</param>
    /// <param name="parentSlot">El ConnectionSlot que llevó a la creación de este chunk (puede ser null para el primer chunk).</param>
    /// <returns>Verdadero si el chunk fue colocado y generado con éxito.</returns>
    private bool TryPlaceChunk(GeneratedChunkData chunkData, Vector2Int chunkGridPos, ConnectionSlot parentSlot)
    {
        OrganicChunkGenerator newChunkInstance = GetChunkFromPool();
        if (newChunkInstance == null)
        {
            Debug.LogError("No se pudo obtener un chunk del pool.");
            return false;
        }

        newChunkInstance.transform.position = chunkData.worldPosition;

        if (generatedLevelMap.ContainsKey(chunkGridPos))
        {
            Debug.LogWarning(
                $"Posición de cuadrícula {chunkGridPos} ya ocupada por un chunk existente. No se puede colocar.");
            ReturnChunkToPool(newChunkInstance);
            return false;
        }

        newChunkInstance.GenerateChunk(chunkData.chunkSeed, chunkData.entryDirection, chunkData.activeExitDirections,
                                       chunkData.terrainHeights, dirtLayerDepth);

        activeChunks.Add(newChunkInstance);
        generatedLevelMap[chunkGridPos] = chunkData;

        foreach (var exitDir in newChunkInstance.ActiveExitDirections)
        {
            Vector2Int targetGridPos = GetTargetGridPosition(chunkGridPos, exitDir);
            if (!generatedLevelMap.ContainsKey(targetGridPos))
            {
                openConnectionSlots.Add(new ConnectionSlot(exitDir, chunkGridPos, newChunkInstance));
            }
        }

        if (parentSlot != null && openConnectionSlots.Contains(parentSlot))
        {
            openConnectionSlots.Remove(parentSlot);
        }

        return true;
    }

    /// <summary>
    /// Activa un chunk existente (lo saca del pool) y lo regenera con sus datos guardados.
    /// </summary>
    /// <param name="chunkData">Los datos del chunk a activar.</param>
    /// <param name="chunkGridPos">La posición en cuadrícula del chunk.</param>
    private void GenerateAndActivateChunk(GeneratedChunkData chunkData, Vector2Int chunkGridPos)
    {
        OrganicChunkGenerator chunk = GetChunkFromPool();
        if (chunk != null)
        {
            chunk.transform.position = chunkData.worldPosition;
            chunk.GenerateChunk(chunkData.chunkSeed, chunkData.entryDirection, chunkData.activeExitDirections,
                                chunkData.terrainHeights, dirtLayerDepth);

            if (!activeChunks.Contains(chunk))
            {
                activeChunks.Add(chunk);
            }

            generatedLevelMap[chunkGridPos] = chunkData;

            foreach (var exitDir in chunkData.activeExitDirections)
            {
                Vector2Int targetGridPos = GetTargetGridPosition(chunkGridPos, exitDir);
                if (!generatedLevelMap.ContainsKey(targetGridPos))
                {
                    bool slotExists = openConnectionSlots.Any(s =>
                        s.parentChunkGridPosition == chunkGridPos && s.connectionDirection == exitDir);
                    if (!slotExists)
                    {
                        openConnectionSlots.Add(new ConnectionSlot(exitDir, chunkGridPos, chunk));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Obtiene una dirección de salida aleatoria válida, excluyendo la dirección de entrada.
    /// </summary>
    private OrganicChunkGenerator.ConnectionDirection GetRandomValidExitDirection(
        OrganicChunkGenerator.ConnectionDirection entryDir)
    {
        List<OrganicChunkGenerator.ConnectionDirection> possibleExits =
            new List<OrganicChunkGenerator.ConnectionDirection>
            {
                OrganicChunkGenerator.ConnectionDirection.North,
                OrganicChunkGenerator.ConnectionDirection.South,
                OrganicChunkGenerator.ConnectionDirection.East,
                OrganicChunkGenerator.ConnectionDirection.West
            };
        possibleExits.Remove(entryDir);

        if (possibleExits.Count == 0) return OrganicChunkGenerator.ConnectionDirection.None;

        return possibleExits[Random.Range(0, possibleExits.Count)];
    }

    /// <summary>
    /// Obtiene la dirección opuesta a una dirección dada.
    /// </summary>
    private OrganicChunkGenerator.ConnectionDirection GetOppositeDirection(
        OrganicChunkGenerator.ConnectionDirection dir)
    {
        return dir switch
        {
            OrganicChunkGenerator.ConnectionDirection.North => OrganicChunkGenerator.ConnectionDirection.South,
            OrganicChunkGenerator.ConnectionDirection.South => OrganicChunkGenerator.ConnectionDirection.North,
            OrganicChunkGenerator.ConnectionDirection.East => OrganicChunkGenerator.ConnectionDirection.West,
            OrganicChunkGenerator.ConnectionDirection.West => OrganicChunkGenerator.ConnectionDirection.East,
            _ => OrganicChunkGenerator.ConnectionDirection.None
        };
    }

    /// <summary>
    /// Genera los datos de altura del terreno para un chunk específico usando Perlin noise.
    /// </summary>
    /// <param name="gridX">Coordenada X de la cuadrícula del chunk.</param>
    /// <param name="gridZ">Coordenada Z de la cuadrícula del chunk.</param>
    /// <param name="chunkSize">Dimensión del chunk.</param>
    /// <returns>Un array 2D de enteros representando la altura de cada (X,Z) dentro del chunk.</returns>
    private int[,] GenerateChunkTerrainHeights(int gridX, int gridZ, int chunkSize)
    {
        int[,] heights = new int[chunkSize, chunkSize];

        for (int x = 0; x < chunkSize; x++)
        {
            for (int z = 0; z < chunkSize; z++)
            {
                float globalX = (gridX * chunkSize) + x;
                float globalZ = (gridZ * chunkSize) + z;

                float noiseValue = Mathf.PerlinNoise(globalX * noiseScale, globalZ * noiseScale);

                int height = Mathf.FloorToInt(noiseValue * maxMountainHeight);

                heights[x, z] = baseTerrainHeight + height;
            }
        }
        return heights;
    }

    void OnDrawGizmos()
    {
        if (openConnectionSlots != null)
        {
            Gizmos.color = Color.blue;
            foreach (var slot in openConnectionSlots)
            {
                if (slot.parentChunkInstance != null && slot.parentChunkInstance.gameObject.activeInHierarchy)
                {
                    Vector3 parentChunkWorldOrigin = GetWorldPositionFromGridPosition(slot.parentChunkGridPosition);
                    Vector3 worldConnPos = parentChunkWorldOrigin +
                                           new Vector3(chunkDimension / 2f, 0, chunkDimension / 2f) +
                                           GetDirectionVector(slot.connectionDirection) * (chunkDimension / 2.0f);

                    Vector3 displayPos = worldConnPos + Vector3.up * 1f;

                    Gizmos.DrawSphere(displayPos, 0.5f);
                    Gizmos.DrawLine(displayPos, displayPos + GetDirectionVector(slot.connectionDirection) * 2f);

#if UNITY_EDITOR
                    Handles.Label(displayPos + Vector3.up * 1.5f,
                        $"To: {GetTargetGridPosition(slot.parentChunkGridPosition, slot.connectionDirection)}");
#endif
                }
            }
        }

        if (activeChunks != null)
        {
            Gizmos.color = Color.red;
            foreach (var chunk in activeChunks)
            {
                if (chunk != null && chunk.gameObject.activeInHierarchy)
                {
                    Vector3 chunkCenter = chunk.transform.position +
                                          new Vector3(chunkDimension / 2f, 0, chunkDimension / 2f);
                    Gizmos.DrawWireCube(chunkCenter, new Vector3(chunkDimension, 1, chunkDimension));

#if UNITY_EDITOR
                    Vector2Int gridPos = GetChunkGridPositionFromWorldPosition(chunk.transform.position);
                    if (generatedLevelMap.TryGetValue(gridPos, out GeneratedChunkData data))
                    {
                        Handles.Label(chunk.transform.position + Vector3.up * 2,
                            $"ID: {data.levelChunkIndex}\nGrid: {gridPos}");
                    }
#endif
                }
            }
        }

        if (chunkPool != null)
        {
            Gizmos.color = Color.gray;
            foreach (var chunk in chunkPool)
            {
                if (chunk != null && chunk.gameObject.activeSelf)
                {
                    Vector3 chunkCenter = chunk.transform.position +
                                          new Vector3(chunkDimension / 2f, 0, chunkDimension / 2f);
                    Gizmos.DrawWireCube(chunkCenter, new Vector3(chunkDimension, 1, chunkDimension));
                }
            }
        }
    }

    /// <summary>
    /// Obtiene un vector de dirección (Vector3) a partir de una ConnectionDirection.
    /// </summary>
    private Vector3 GetDirectionVector(OrganicChunkGenerator.ConnectionDirection dir)
    {
        return dir switch
        {
            OrganicChunkGenerator.ConnectionDirection.North => Vector3.forward,
            OrganicChunkGenerator.ConnectionDirection.South => Vector3.back,
            OrganicChunkGenerator.ConnectionDirection.East => Vector3.right,
            OrganicChunkGenerator.ConnectionDirection.West => Vector3.left,
            _ => Vector3.zero
        };
    }
}