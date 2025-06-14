using UnityEngine;
using System.Collections.Generic;
using System.Linq; // Necesario para .Any() y .FirstOrDefault()

// Necesario para Gizmos en el editor (si usas Handles.Label)
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Gestiona la generación dinámica del nivel, el object pooling de chunks,
/// y la activación/desactivación de chunks basada en la posición del jugador.
/// </summary>
public class LevelGeneratorManager : MonoBehaviour
{
    [Header("Generation Settings")] [Tooltip("Semilla global para la generación del nivel (0 para aleatorio).")]
    public int globalSeed = 0;

    [Tooltip("Prefab del chunk orgánico (debe tener el script OrganicChunkGenerator).")]
    public GameObject organicChunkPrefab;

    [Tooltip("Dimensión de un solo chunk (ej. 13 para 13x13).")]
    public int chunkDimension = 13;

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

    // Pool de chunks para reutilización
    private List<OrganicChunkGenerator>
        chunkPool = new List<OrganicChunkGenerator>(); // Chunks inactivos, listos para usar

    private List<OrganicChunkGenerator>
        activeChunks = new List<OrganicChunkGenerator>(); // Chunks actualmente en la escena y activos

    // Mapa de todos los chunks generados para persistencia (clave: posición en cuadrícula)
    private Dictionary<Vector2Int, GeneratedChunkData> generatedLevelMap =
        new Dictionary<Vector2Int, GeneratedChunkData>();

    private int nextChunkUniqueIndex = 0; // Un índice único para cada chunk generado globalmente

    // El chunk actual del jugador (coordenadas de la cuadrícula del chunk)
    private Vector2Int currentChunkPlayerIsInGrid = Vector2Int.zero;

    /// <summary>
    /// Clase interna para representar un slot de conexión abierto donde se puede generar un nuevo chunk.
    /// </summary>
    private class ConnectionSlot
    {
        public OrganicChunkGenerator.ConnectionDirection connectionDirection; // Dirección de salida del chunk padre
        public Vector2Int parentChunkGridPosition; // Posición en cuadrícula del chunk padre

        public OrganicChunkGenerator
            parentChunkInstance; // Instancia del chunk padre (útil para referenciar y verificar actividad)

        public ConnectionSlot(OrganicChunkGenerator.ConnectionDirection dir, Vector2Int parentGridPos,
            OrganicChunkGenerator parentInstance)
        {
            connectionDirection = dir;
            parentChunkGridPosition = parentGridPos;
            parentChunkInstance = parentInstance;
        }
    }

    private List<ConnectionSlot>
        openConnectionSlots = new List<ConnectionSlot>(); // Lista de slots disponibles para generar nuevos chunks

    void Start()
    {
        InitializeChunkPool(maxActiveChunksWindow + 2); // Inicializar con algunos chunks extra
        GenerateInitialLevel(); // Generar el nivel inicial
    }

    void Update()
    {
        if (playerReference == null)
        {
            Debug.LogWarning("Player Reference no asignado en LevelGeneratorManager. No se puede actualizar el nivel.");
            return;
        }

        // Determinar en qué chunk de la cuadrícula está el jugador
        Vector2Int newPlayerChunkGrid = GetChunkGridPositionFromWorldPosition(playerReference.transform.position);

        // Si el jugador ha cambiado de chunk en la cuadrícula
        if (newPlayerChunkGrid != currentChunkPlayerIsInGrid)
        {
            currentChunkPlayerIsInGrid = newPlayerChunkGrid; // Actualizar la posición del chunk del jugador
            Debug.Log($"Jugador ha entrado al chunk de la cuadrícula: {currentChunkPlayerIsInGrid}");
            UpdateLevelChunks(); // Re-evaluar y actualizar los chunks activos
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

            chunkGO.SetActive(false); // <--- Clave: Iniciar desactivado
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
        if (chunkPool.Any()) // Si hay chunks disponibles en el pool
        {
            OrganicChunkGenerator chunk = chunkPool[0]; // Tomar el primero
            chunkPool.RemoveAt(0); // Removerlo del pool
            chunk.gameObject.SetActive(true); // <--- Clave: Activarlo
            return chunk;
        }
        else // Si el pool está vacío, crear uno nuevo (esto debería ser raro si el pool es lo suficientemente grande)
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
        if (chunk != null && !chunkPool.Contains(chunk)) // Evitar errores si el chunk es nulo o ya está en el pool
        {
            chunk.gameObject.SetActive(false); // <--- Clave: Desactivarlo
            chunk.transform.position = Vector3.zero; // Opcional: Resetear posición para limpiar el editor
            chunkPool.Add(chunk); // Añadirlo de nuevo al pool
            activeChunks.Remove(chunk); // Removerlo de la lista de chunks activos
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

        // 1. Crear el primer chunk (Inicio) en (0,0) de la cuadrícula
        Vector2Int initialGridPos = Vector2Int.zero;
        Vector3 initialWorldPos = GetWorldPositionFromGridPosition(initialGridPos);

        // El primer chunk es siempre desde el centro, con una salida aleatoria
        GeneratedChunkData firstChunkData = new GeneratedChunkData(initialWorldPos,
            Random.Range(int.MinValue, int.MaxValue), OrganicChunkGenerator.ConnectionDirection.Center,
            nextChunkUniqueIndex++);
        firstChunkData.activeExitDirections.Add(
            GetRandomValidExitDirection(OrganicChunkGenerator.ConnectionDirection
                .Center)); // Una salida inicial aleatoria

        // Generar y activar el primer chunk
        GenerateAndActivateChunk(firstChunkData, initialGridPos);
        currentChunkPlayerIsInGrid = initialGridPos; // El jugador comienza en este chunk

        // 2. Mover al jugador al inicio del camino del chunk (0,0)
        if (movePlayerToStart && playerReference != null)
        {
            // Obtener la instancia del chunk que acabamos de generar
            OrganicChunkGenerator initialChunkInstance = activeChunks.FirstOrDefault(c =>
                GetChunkGridPositionFromWorldPosition(c.transform.position) == initialGridPos);

            if (initialChunkInstance != null)
            {
                // Usamos GetPathInternalPoint para obtener una posición dentro del camino del chunk
                Vector2Int localEntryPoint = initialChunkInstance.EntryPoint;
                OrganicChunkGenerator.ConnectionDirection entryDir = initialChunkInstance.EntryDirection;
                Vector2Int localInternalPathStart =
                    initialChunkInstance.GetPathInternalPoint(localEntryPoint, entryDir);

                // Convertir la posición local (0-chunkSize) a posición mundial
                Vector3 startPosition =
                    new Vector3(localInternalPathStart.x, playerReference.transform.position.y,
                        localInternalPathStart.y) + initialChunkInstance.transform.position;

                playerReference.transform.position = startPosition;
                Debug.Log($"Jugador movido a la posición de inicio del chunk (0,0): {startPosition}");
            }
            else
            {
                Debug.LogWarning("No se encontró la instancia del chunk inicial para mover al jugador.");
            }
        }

        // 3. Generar los siguientes chunks para llenar la ventana inicial
        UpdateLevelChunks(); // La primera llamada a UpdateLevelChunks se encargará de generar los vecinos iniciales
    }

    /// <summary>
    /// Actualiza los chunks activos en el nivel basándose en la posición actual del jugador.
    /// Recicla los chunks fuera de la ventana y genera/reactiva los nuevos.
    /// </summary>
    private void UpdateLevelChunks()
    {
        // Paso 1: Identificar las posiciones de cuadrícula que deberían estar activas (la "ventana" alrededor del jugador)
        HashSet<Vector2Int> desiredActiveGridPositions = new HashSet<Vector2Int>();
        // Añadir el chunk actual del jugador
        desiredActiveGridPositions.Add(currentChunkPlayerIsInGrid);

        // Añadir vecinos dentro de la "ventana" de MaxActiveChunksWindow
        for (int x = -(maxActiveChunksWindow / 2); x <= (maxActiveChunksWindow / 2); x++)
        {
            for (int y = -(maxActiveChunksWindow / 2); y <= (maxActiveChunksWindow / 2); y++)
            {
                Vector2Int relativePos = new Vector2Int(x, y);
                // Usar distancia Manhattan para crear una forma de diamante/cuadrado girado
                if (Mathf.Abs(relativePos.x) + Mathf.Abs(relativePos.y) <= maxActiveChunksWindow / 2)
                {
                    desiredActiveGridPositions.Add(currentChunkPlayerIsInGrid + relativePos);
                }
            }
        }

        // Paso 2: Desactivar chunks que NO están en la lista de deseados (y que están actualmente activos)
        List<OrganicChunkGenerator> chunksToDeactivate = new List<OrganicChunkGenerator>();
        foreach (var chunkInstance in activeChunks)
        {
            Vector2Int chunkGridPos = GetChunkGridPositionFromWorldPosition(chunkInstance.transform.position);
            if (!desiredActiveGridPositions.Contains(chunkGridPos)) // Si el chunk activo no está en la ventana deseada
            {
                chunksToDeactivate.Add(chunkInstance);
            }
        }

        foreach (var chunkInstance in chunksToDeactivate)
        {
            Debug.Log(
                $"Desactivando chunk en {chunkInstance.transform.position} (Grid: {GetChunkGridPositionFromWorldPosition(chunkInstance.transform.position)})");
            ReturnChunkToPool(chunkInstance); // Devuelve al pool y desactiva el GameObject
        }

        // Paso 3: Activar o generar chunks que SÍ están en la lista de deseados
        foreach (Vector2Int gridPos in desiredActiveGridPositions)
        {
            // Intentar encontrar el chunk activo (ya está en escena y activo)
            OrganicChunkGenerator activeInstance = activeChunks.FirstOrDefault(c =>
                GetChunkGridPositionFromWorldPosition(c.transform.position) == gridPos);

            if (activeInstance == null) // Si no está activo (necesita ser activado o generado)
            {
                if (generatedLevelMap.TryGetValue(gridPos,
                        out GeneratedChunkData chunkData)) // Si ya lo generamos antes (en el mapa)
                {
                    Debug.Log($"Reactivando chunk existente en Grid: {gridPos} (ID: {chunkData.levelChunkIndex})");
                    GenerateAndActivateChunk(chunkData, gridPos); // Reutilizar el chunk del pool y regenerarlo
                }
                else // Chunk nunca antes generado, hay que crearlo por primera vez
                {
                    // Necesitamos una ConnectionSlot para generar un nuevo chunk
                    // Esto recorre los slots de conexión abiertos por chunks existentes que apunten a esta gridPos
                    ConnectionSlot slotToUse = openConnectionSlots.FirstOrDefault(s =>
                        GetTargetGridPosition(s.parentChunkGridPosition, s.connectionDirection) == gridPos);

                    if (slotToUse != null)
                    {
                        // Determinar las salidas para el nuevo chunk (incluyendo lógica de bifurcaciones)
                        List<OrganicChunkGenerator.ConnectionDirection> desiredExits =
                            new List<OrganicChunkGenerator.ConnectionDirection>();
                        OrganicChunkGenerator.ConnectionDirection newChunkEntryDir =
                            GetOppositeDirection(slotToUse.connectionDirection);

                        // Lógica para intentar una bifurcación
                        bool tryFork = (forkFrequency > 0 && nextChunkUniqueIndex % forkFrequency == 0);

                        if (tryFork)
                        {
                            OrganicChunkGenerator.ConnectionDirection mainExit =
                                GetRandomValidExitDirection(newChunkEntryDir);
                            if (mainExit != OrganicChunkGenerator.ConnectionDirection.None)
                            {
                                desiredExits.Add(mainExit);
                            }

                            // Intentar añadir una segunda salida (bifurcación)
                            List<OrganicChunkGenerator.ConnectionDirection> possibleForkDirs =
                                new List<OrganicChunkGenerator.ConnectionDirection>
                                {
                                    OrganicChunkGenerator.ConnectionDirection.North,
                                    OrganicChunkGenerator.ConnectionDirection.South,
                                    OrganicChunkGenerator.ConnectionDirection.East,
                                    OrganicChunkGenerator.ConnectionDirection.West
                                };
                            possibleForkDirs.Remove(newChunkEntryDir); // No puede bifurcarse hacia la entrada
                            possibleForkDirs
                                .Remove(mainExit); // No puede bifurcarse en la misma dirección que la salida principal

                            if (possibleForkDirs.Any())
                            {
                                OrganicChunkGenerator.ConnectionDirection forkExit =
                                    possibleForkDirs[Random.Range(0, possibleForkDirs.Count)];
                                desiredExits.Add(forkExit);
                                Debug.Log($"Chunk a generar en {gridPos} tiene bifurcación en {forkExit}");
                            }
                        }

                        if (!desiredExits.Any()) // Asegurarse de que siempre haya al menos una salida
                        {
                            desiredExits.Add(GetRandomValidExitDirection(newChunkEntryDir));
                        }

                        // Crear los datos para el nuevo chunk
                        Vector3 newChunkWorldPos = GetWorldPositionFromGridPosition(gridPos);
                        GeneratedChunkData newChunkData = new GeneratedChunkData(newChunkWorldPos,
                            Random.Range(int.MinValue, int.MaxValue), newChunkEntryDir, nextChunkUniqueIndex++);
                        newChunkData.activeExitDirections = desiredExits; // Asignar las salidas determinadas

                        // Intentar colocar y activar el nuevo chunk
                        bool chunkPlaced = TryPlaceChunk(newChunkData, gridPos, slotToUse);
                        if (chunkPlaced)
                        {
                            // El slot se elimina dentro de TryPlaceChunk si la colocación es exitosa
                        }
                    }
                    // else: Si no hay un slot de conexión apuntando a esta `gridPos` y el chunk no existe,
                    // significa que aún no se ha llegado a esta área por un camino válido. No hacemos nada.
                }
            }
        }

        // Paso 4: Limpiar openConnectionSlots
        // Remover slots si su chunk padre ya no está activo, o si el slot ya fue usado para generar un chunk
        openConnectionSlots.RemoveAll(slot =>
                !activeChunks.Contains(slot.parentChunkInstance) || // El chunk padre del slot no está activo
                generatedLevelMap.ContainsKey(GetTargetGridPosition(slot.parentChunkGridPosition,
                    slot.connectionDirection)) // El chunk que generaría este slot ya existe
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
            _ => parentGridPos // Centro o None
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

        // Antes de generar, verificar si esta posición de cuadrícula ya está ocupada
        if (generatedLevelMap.ContainsKey(chunkGridPos))
        {
            Debug.LogWarning(
                $"Posición de cuadrícula {chunkGridPos} ya ocupada por un chunk existente. No se puede colocar.");
            ReturnChunkToPool(newChunkInstance);
            return false; // No se puede colocar aquí
        }

        // Generar el chunk con sus datos
        newChunkInstance.GenerateChunk(chunkData.chunkSeed, chunkData.entryDirection, chunkData.activeExitDirections);

        // Añadir a las listas de gestión
        activeChunks.Add(newChunkInstance);
        generatedLevelMap[chunkGridPos] = chunkData; // Guardar los datos en el mapa usando la posición de la cuadrícula

        // Añadir nuevas conexiones para las salidas de este chunk
        foreach (var exitDir in newChunkInstance.ActiveExitDirections)
        {
            // Asegurarse de no añadir un slot si el destino ya existe en el mapa (ej. se conectó con un chunk preexistente)
            Vector2Int targetGridPos = GetTargetGridPosition(chunkGridPos, exitDir);
            if (!generatedLevelMap.ContainsKey(targetGridPos))
            {
                openConnectionSlots.Add(new ConnectionSlot(exitDir, chunkGridPos, newChunkInstance));
            }
        }

        // Si el slot fue usado con éxito, removerlo de openConnectionSlots
        if (parentSlot != null && openConnectionSlots.Contains(parentSlot))
        {
            openConnectionSlots.Remove(parentSlot);
        }

        return true; // Colocado con éxito
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
            chunk.GenerateChunk(chunkData.chunkSeed, chunkData.entryDirection, chunkData.activeExitDirections);

            // Asegurarse de que no se duplique en activeChunks si ya estaba ahí
            if (!activeChunks.Contains(chunk))
            {
                activeChunks.Add(chunk);
            }

            // Asegurarse de que esté en el mapa (debería estar si se reactiva)
            // Esto es importante si un chunk fue "desactivado" de activeChunks pero aún no se eliminó del mapa por alguna razón.
            generatedLevelMap[chunkGridPos] = chunkData;

            // Actualizar openConnectionSlots con las salidas de este chunk (si se está volviendo a activar)
            foreach (var exitDir in chunkData.activeExitDirections)
            {
                Vector2Int targetGridPos = GetTargetGridPosition(chunkGridPos, exitDir);
                // Solo añadir el slot si el chunk de destino no existe ya en el mapa
                if (!generatedLevelMap.ContainsKey(targetGridPos))
                {
                    // Evitar añadir slots duplicados
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
        possibleExits.Remove(entryDir); // Elimina la dirección de entrada de las opciones

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
            _ => OrganicChunkGenerator.ConnectionDirection.None // Para Center o None
        };
    }

    // --- Funciones de Depuración y Gizmos ---

    void OnDrawGizmos()
    {
        // Dibujar slots de conexión abiertos (para ver dónde se pueden generar nuevos chunks)
        if (openConnectionSlots != null)
        {
            Gizmos.color = Color.blue;
            foreach (var slot in openConnectionSlots)
            {
                // Solo dibujar si el padre está activo
                if (slot.parentChunkInstance != null && slot.parentChunkInstance.gameObject.activeInHierarchy)
                {
                    // Calcular la posición mundial del centro de la conexión
                    Vector3 parentChunkWorldOrigin = GetWorldPositionFromGridPosition(slot.parentChunkGridPosition);
                    Vector3 worldConnPos = parentChunkWorldOrigin +
                                           new Vector3(chunkDimension / 2f, 0, chunkDimension / 2f) +
                                           GetDirectionVector(slot.connectionDirection) * (chunkDimension / 2.0f);

                    // Ajuste para dibujar en el centro de la pared de conexión a la altura de la pared
                    Vector3 displayPos = worldConnPos + Vector3.up * 1f; // A la altura de la pared

                    Gizmos.DrawSphere(displayPos, 0.5f);
                    Gizmos.DrawLine(displayPos, displayPos + GetDirectionVector(slot.connectionDirection) * 2f);

#if UNITY_EDITOR
                    // Dibuja la posición de la cuadrícula del destino para depuración
                    Handles.Label(displayPos + Vector3.up * 1.5f,
                        $"To: {GetTargetGridPosition(slot.parentChunkGridPosition, slot.connectionDirection)}");
#endif
                }
            }
        }

        // Dibujar chunks activos (para visualizar la ventana de chunks)
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
                    // Dibuja el índice y la posición de la cuadrícula del chunk para depuración
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

        // Puedes dibujar los chunks del pool en otro color si están activos en el editor
        // (Normalmente no deberían estar activos, pero por si acaso para depuración)
        if (chunkPool != null)
        {
            Gizmos.color = Color.gray;
            foreach (var chunk in chunkPool)
            {
                if (chunk != null && chunk.gameObject.activeSelf) // Si por alguna razón está activo en el editor
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