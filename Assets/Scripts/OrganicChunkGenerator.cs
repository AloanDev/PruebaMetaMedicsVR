using UnityEngine;
using System.Collections.Generic;
using System.Linq; // Necesario para .Any() y .Last()

/// <summary>
/// Gestiona la generación de contenido (suelos y paredes, y caminos) dentro de un chunk individual.
/// </summary>
public class OrganicChunkGenerator : MonoBehaviour
{
    [Header("Chunk Settings")] [Tooltip("Dimensiones del chunk (ej. 13x13 unidades).")]
    public int chunkSize = 13;

    private int m_Seed; // Semilla de generación para este chunk específico

    [Range(0.0f, 1.0f)]
    [Tooltip("Controla la aleatoriedad en la generación del camino (0.0f = recto, 1.0f = muy errático).")]
    public float randomness = 0.0f; // Mantener a 0.0f para un ancho estricto de 1 cubo

    private const int MMaxAttempts = 2000; // Máximo de intentos para generar segmentos de camino

    [Header("Prefabs")] public GameObject floorPrefab; // Prefab para los bloques de suelo
    public GameObject wallPrefab; // Prefab para los bloques de pared (asegúrate de que tenga el tag "Wall")

    /// <summary>
    /// Enumera las posibles direcciones de conexión/salida de un chunk.
    /// </summary>
    public enum ConnectionDirection
    {
        North, // +Z
        South, // -Z
        East, // +X
        West, // -X
        Center, // Usado para el chunk inicial que no tiene una dirección de entrada de otro chunk
        None // Sin conexión o dirección inválida
    }

    // Propiedades para almacenar la información de las conexiones del chunk actual
    public Vector2Int EntryPoint { get; private set; } // Punto local (en la cuadrícula del chunk) de entrada
    public ConnectionDirection EntryDirection { get; private set; } // Dirección de entrada

    public List<Vector2Int> ActiveExitPoints { get; private set; } = new List<Vector2Int>(); // Puntos locales de salida

    public List<ConnectionDirection> ActiveExitDirections { get; private set; } =
        new List<ConnectionDirection>(); // Direcciones de salida

    /// <summary>
    /// Genera el contenido del chunk basándose en una semilla, dirección de entrada y salidas deseadas.
    /// </summary>
    /// <param name="seed">La semilla para la generación de este chunk.</param>
    /// <param name="entryDir">La dirección por la que el jugador entró o se conectará a este chunk.</param>
    /// <param name="desiredExits">Una lista de direcciones por las que se desea que el chunk tenga salidas.</param>
    public void GenerateChunk(int seed, ConnectionDirection entryDir, List<ConnectionDirection> desiredExits)
    {
        m_Seed = seed;
        Random.InitState(m_Seed);

        Debug.Log(
            $"Generando chunk en {transform.position} con semilla: {m_Seed}, Entrada: {entryDir}, Salidas deseadas: {string.Join(", ", desiredExits)}");

        GenerateBase(); // Crea el suelo y las paredes básicas

        EntryDirection = entryDir;
        EntryPoint = GetEdgePoint(entryDir); // Obtiene el punto de borde para la entrada

        ActiveExitPoints.Clear(); // Limpia las salidas anteriores
        ActiveExitDirections.Clear();

        Vector2Int
            internalPathStart =
                GetPathInternalPoint(EntryPoint, EntryDirection); // Punto de inicio del camino dentro del chunk

        // Si no se especifican salidas deseadas, generar una aleatoria (comportamiento de fallback)
        if (desiredExits == null || desiredExits.Count == 0)
        {
            Debug.LogWarning("GenerateChunk llamado sin salidas deseadas. Generando una salida aleatoria.");
            ConnectionDirection fallbackExit = GetRandomValidExitDirection(EntryDirection);
            desiredExits = new List<ConnectionDirection> { fallbackExit };
        }

        // Generar caminos para cada salida deseada
        foreach (var exitDir in desiredExits)
        {
            if (exitDir == EntryDirection) // Evitar que una salida sea la misma que la entrada
            {
                Debug.LogWarning($"Evitando que la salida deseada sea la misma que la entrada: {exitDir}");
                continue;
            }

            Vector2Int currentExitPoint = GetEdgePoint(exitDir); // Punto de borde para esta salida
            Vector2Int currentInternalExit =
                GetPathInternalPoint(currentExitPoint, exitDir); // Punto de salida del camino dentro del chunk

            List<Vector2Int> pathSegment = new List<Vector2Int>(); // Lista para almacenar los puntos del camino
            Vector2Int
                midPoint = GetMidPointForForcedTurn(internalPathStart,
                    currentInternalExit); // Intenta encontrar un punto intermedio para forzar un giro en L

            if (midPoint.x != -1) // Si se encontró un midPoint válido
            {
                TraversePathSegment(internalPathStart, midPoint,
                    pathSegment); // Primero segmento: inicio a punto intermedio
                Vector2Int current = pathSegment.Any() ? pathSegment.Last() : internalPathStart;
                if (current != currentInternalExit)
                {
                    TraversePathSegment(current, currentInternalExit,
                        pathSegment); // Segundo segmento: punto intermedio a salida
                }
            }
            else // Si no hay midPoint, intenta un camino directo
            {
                TraversePathSegment(internalPathStart, currentInternalExit, pathSegment);
            }

            // Asegurarse de que el camino realmente llegó al punto de salida
            Vector2Int finalCurrent = pathSegment.Any() ? pathSegment.Last() : internalPathStart;
            if (finalCurrent != currentInternalExit)
            {
                Debug.LogWarning($"Chunk {name}: El camino a {exitDir} no llegó al final, realizando limpieza final.");
                ForceCompletePath(finalCurrent, currentInternalExit, pathSegment);
            }

            DestroyWallAt(currentExitPoint); // Destruye la pared en el punto de salida

            // Añadir a las listas de salidas activas del chunk
            ActiveExitPoints.Add(currentExitPoint);
            ActiveExitDirections.Add(exitDir);
        }

        // Destruir la pared en el punto de entrada (si no es el centro)
        if (EntryDirection != ConnectionDirection.Center && EntryPoint.x != -1)
        {
            DestroyWallAt(EntryPoint);
        }
    }

    /// <summary>
    /// Genera la base del chunk (suelo y paredes sólidas en todas las posiciones).
    /// Destruye cualquier objeto hijo existente primero.
    /// </summary>
    private void GenerateBase()
    {
        // Limpiar objetos hijos existentes (útil para la reutilización del pool)
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }

        // Instanciar el suelo y las paredes en cada posición del chunk
        for (int x = 0; x < chunkSize; x++)
        {
            for (int z = 0; z < chunkSize; z++)
            {
                Instantiate(floorPrefab, new Vector3(x, 0, z) + transform.position, Quaternion.identity, transform);
                Instantiate(wallPrefab, new Vector3(x, 1, z) + transform.position, Quaternion.identity, transform);
            }
        }
    }

    /// <summary>
    /// Traza un segmento de camino entre dos puntos, evitando caminos duplicados.
    /// </summary>
    /// <param name="segmentStart">Punto de inicio del segmento (local).</param>
    /// <param name="segmentEnd">Punto final del segmento (local).</param>
    /// <param name="path">Lista que almacena los puntos del camino generados.</param>
    private void TraversePathSegment(Vector2Int segmentStart, Vector2Int segmentEnd, List<Vector2Int> path)
    {
        Vector2Int currentSegmentPoint = segmentStart;
        int attempts = 0;

        // Asegúrate de que el punto de inicio del segmento esté en el camino y su pared destruida
        if (!path.Contains(segmentStart))
        {
            path.Add(segmentStart);
            DestroyWallAt(segmentStart);
        }
        else
        {
            DestroyWallAt(segmentStart); // Si ya está, solo asegura que la pared esté destruida
        }

        // Recorrer el camino hasta llegar al final o agotar intentos
        while (currentSegmentPoint != segmentEnd && attempts < MMaxAttempts)
        {
            Vector2Int next = GetNextStep(currentSegmentPoint, segmentEnd, path);

            if (next == currentSegmentPoint) // Si no se pudo mover, el camino está atascado
            {
                Debug.LogWarning($"Segmento atascado de {currentSegmentPoint} a {segmentEnd}. Forzando camino.");
                ForceCompletePath(currentSegmentPoint, segmentEnd, path); // Forzar la finalización
                currentSegmentPoint = segmentEnd; // Forzar que el camino se complete para salir del bucle
                break;
            }

            path.Add(next);
            currentSegmentPoint = next;
            DestroyWallAt(currentSegmentPoint); // Destruir la pared en el nuevo punto del camino
            attempts++;
        }

        // Si por alguna razón el camino no llegó al final, forzar la finalización
        if (currentSegmentPoint != segmentEnd)
        {
            ForceCompletePath(currentSegmentPoint, segmentEnd, path);
        }
    }

    /// <summary>
    /// Determina el siguiente paso en el camino hacia un punto final.
    /// Intenta moverse hacia el objetivo con preferencia, o aleatoriamente si no es posible.
    /// </summary>
    /// <param name="current">Punto actual (local).</param>
    /// <param name="end">Punto final objetivo (local).</param>
    /// <param name="path">Lista de puntos ya en el camino para evitar duplicados.</param>
    /// <returns>El siguiente punto al que moverse, o el punto actual si está atascado.</returns>
    private Vector2Int GetNextStep(Vector2Int current, Vector2Int end, List<Vector2Int> path)
    {
        Vector2Int[] orthogonalDirections = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

        List<Vector2Int> validSteps = new List<Vector2Int>();

        int deltaX = end.x - current.x;
        int deltaY = end.y - current.y;

        bool canMoveX = Mathf.Abs(deltaX) > 0;
        bool canMoveY = Mathf.Abs(deltaY) > 0;

        Vector2Int primaryDirection = Vector2Int.zero;

        // Intentar moverse hacia el objetivo con una preferencia (X o Y)
        if (canMoveX && canMoveY)
        {
            if (Random.value < 0.5f)
            {
                primaryDirection = new Vector2Int(Mathf.Sign(deltaX) > 0 ? 1 : -1, 0);
            }
            else
            {
                primaryDirection = new Vector2Int(0, Mathf.Sign(deltaY) > 0 ? 1 : -1);
            }
        }
        else if (canMoveX)
        {
            primaryDirection = new Vector2Int(Mathf.Sign(deltaX) > 0 ? 1 : -1, 0);
        }
        else if (canMoveY)
        {
            primaryDirection = new Vector2Int(0, Mathf.Sign(deltaY) > 0 ? 1 : -1);
        }

        if (primaryDirection != Vector2Int.zero)
        {
            Vector2Int potentialStep = current + primaryDirection;
            // Solo toma el paso primario si está en límites, no está ya en el camino y la aleatoriedad lo permite
            if (IsPathPointInBounds(potentialStep) && !path.Contains(potentialStep))
            {
                if (Random.value > randomness) // Si randomness es 0, siempre lo toma
                {
                    return potentialStep;
                }
            }
        }

        // Si el paso primario no es válido o la aleatoriedad lo impide, busca pasos ortogonales aleatorios
        foreach (var dir in orthogonalDirections)
        {
            Vector2Int step = current + dir;
            if (IsPathPointInBounds(step) && !path.Contains(step))
            {
                validSteps.Add(step);
            }
        }

        if (validSteps.Count > 0)
        {
            return validSteps[Random.Range(0, validSteps.Count)];
        }

        return current; // Atascado, no puede moverse
    }

    /// <summary>
    /// Fuerza la finalización de un camino conectando directamente los puntos.
    /// Utilizado como fallback si el algoritmo orgánico se atasca.
    /// </summary>
    /// <param name="from">Punto de inicio actual (local).</param>
    /// <param name="to">Punto final objetivo (local).</param>
    /// <param name="path">Lista de puntos del camino a la que añadir.</param>
    private void ForceCompletePath(Vector2Int from, Vector2Int to, List<Vector2Int> path)
    {
        Vector2Int current = from;

        while (current != to)
        {
            Vector2Int next = current;

            // Mover en X primero, luego en Y si no se ha alcanzado la X deseada
            if (current.x != to.x)
            {
                next.x += (to.x > current.x) ? 1 : -1;
            }
            else if (current.y != to.y)
            {
                next.y += (to.y > current.y) ? 1 : -1;
            }

            if (next == current) break; // Evitar bucle infinito si no hay movimiento

            // Asegurar que el punto sea válido antes de añadirlo
            if (IsPathPointInBounds(next) && !path.Contains(next))
            {
                path.Add(next);
                DestroyWallAt(next);
            }
            else if
                (next == to) // Caso especial si el destino ya está en el camino, pero queremos asegurarnos de procesarlo
            {
                if (!path.Contains(next))
                {
                    path.Add(next);
                    DestroyWallAt(next);
                }

                current = to; // Ya hemos llegado al destino, salir
                break;
            }
            else
            {
                // Si el punto no es válido (fuera de límites o ya en el camino de FORMA INESPERADA), salir
                Debug.LogWarning(
                    $"ForceCompletePath: No se pudo añadir el punto {next} (fuera de límites o ya en el camino).");
                break;
            }

            current = next; // Avanzar al siguiente punto
        }
    }

    /// <summary>
    /// Intenta encontrar un punto intermedio para forzar un giro en el camino (en forma de L).
    /// Prioriza los giros "naturales" y luego busca puntos aleatorios.
    /// </summary>
    /// <param name="start">Punto de inicio del camino (local).</param>
    /// <param name="end">Punto final del camino (local).</param>
    /// <returns>Un punto intermedio local, o Vector2Int(-1, -1) si no se encontró uno adecuado.</returns>
    private Vector2Int GetMidPointForForcedTurn(Vector2Int start, Vector2Int end)
    {
        int minInnerBound = 1; // Borde interior del chunk (para que el camino no esté en el borde exacto)
        int maxInnerBound = chunkSize - 2;

        List<Vector2Int> potentialMidPoints = new List<Vector2Int>();

        Vector2Int candidate1 = new Vector2Int(start.x, end.y); // Punto de giro en L (X de inicio, Y de fin)
        Vector2Int candidate2 = new Vector2Int(end.x, start.y); // Punto de giro en L (X de fin, Y de inicio)

        // Si los puntos de inicio y fin no están alineados (se requiere un giro natural en L)
        if (start.x != end.x && start.y != end.y)
        {
            if (IsPathPointInBounds(candidate1) && candidate1 != start && candidate1 != end)
                potentialMidPoints.Add(candidate1);
            if (IsPathPointInBounds(candidate2) && candidate2 != start && candidate2 != end)
                potentialMidPoints.Add(candidate2);
        }
        else // Si están alineados (se requiere un "desvío" para un giro forzado)
        {
            // Solo añadir si no son colineales con el segmento original
            if (IsPathPointInBounds(candidate1) && candidate1 != start && candidate1 != end)
            {
                if (!IsCollinear(start, end, candidate1))
                {
                    potentialMidPoints.Add(candidate1);
                }
            }

            if (IsPathPointInBounds(candidate2) && candidate2 != start && candidate2 != end && candidate2 != candidate1)
            {
                if (!IsCollinear(start, end, candidate2))
                {
                    potentialMidPoints.Add(candidate2);
                }
            }
        }

        if (potentialMidPoints.Any()) // Si encontramos candidatos de giro "naturales"
        {
            return potentialMidPoints[Random.Range(0, potentialMidPoints.Count)];
        }

        // Si no se encontraron puntos de giro naturales, intentar encontrar un punto de giro aleatorio
        // dentro del chunk que fuerce un giro.
        for (int i = 0; i < 100; i++)
        {
            int randX = Random.Range(minInnerBound, maxInnerBound + 1);
            int randY = Random.Range(minInnerBound, maxInnerBound + 1);
            Vector2Int randomMid = new Vector2Int(randX, randY);

            // El punto aleatorio debe estar dentro de los límites internos, no ser el inicio/fin,
            // y no ser colineal con el segmento inicio-fin para forzar un giro.
            if (randomMid != start && randomMid != end &&
                !IsCollinear(start, end, randomMid))
            {
                return randomMid;
            }
        }

        Debug.LogWarning(
            $"No se pudo encontrar un midPoint de giro válido para start:{start} y end:{end} en chunk {transform.position}. Generando camino recto como fallback.");
        return new Vector2Int(-1, -1); // Indicar que no se encontró un midPoint de giro
    }

    /// <summary>
    /// Comprueba si tres puntos son colineales (están en la misma línea horizontal o vertical).
    /// </summary>
    private bool IsCollinear(Vector2Int p1, Vector2Int p2, Vector2Int p3)
    {
        // Verdadero si los tres puntos están en la misma línea horizontal o vertical
        return (p1.x == p2.x && p3.x == p1.x) || (p1.y == p2.y && p3.y == p1.y);
    }

    /// <summary>
    /// Comprueba si un punto dado está dentro de los límites internos del chunk.
    /// </summary>
    private bool IsPathPointInBounds(Vector2Int point)
    {
        int minBound = 1; // Dejar un borde de paredes
        int maxBound = chunkSize - 2; // El borde opuesto

        return point.x >= minBound && point.x <= maxBound &&
               point.y >= minBound && point.y <= maxBound;
    }

    /// <summary>
    /// Obtiene el punto de borde (en coordenadas locales del chunk) para una dirección de conexión dada.
    /// </summary>
    private Vector2Int GetEdgePoint(ConnectionDirection side)
    {
        int center = chunkSize / 2;
        switch (side)
        {
            case ConnectionDirection.North: return new Vector2Int(center, chunkSize - 1);
            case ConnectionDirection.South: return new Vector2Int(center, 0);
            case ConnectionDirection.East: return new Vector2Int(chunkSize - 1, center);
            case ConnectionDirection.West: return new Vector2Int(0, center);
            case ConnectionDirection.Center: return new Vector2Int(center, center);
            case ConnectionDirection.None: return new Vector2Int(-1, -1); // Valor de error/nulo
            default: return Vector2Int.zero;
        }
    }

    /// <summary>
    /// Calcula un punto "interno" para el inicio/fin de un camino,
    /// dando un pequeño offset desde el borde del chunk.
    /// </summary>
    /// <param name="edgePoint">El punto de borde del chunk.</param>
    /// <param name="edgeDirection">La dirección de la conexión en ese borde.</param>
    /// <returns>Un punto local un paso dentro del chunk desde el borde.</returns>
    public Vector2Int GetPathInternalPoint(Vector2Int edgePoint, ConnectionDirection edgeDirection)
    {
        int innerOffset = 1; // Un paso hacia adentro desde el borde

        if (edgeDirection == ConnectionDirection.Center || edgePoint.x == -1)
        {
            return edgePoint; // Si es el centro o un punto no válido, no hay offset
        }

        // Mover el punto un paso hacia adentro del chunk
        if (edgeDirection == ConnectionDirection.North)
        {
            return new Vector2Int(edgePoint.x, edgePoint.y - innerOffset);
        }
        else if (edgeDirection == ConnectionDirection.South)
        {
            return new Vector2Int(edgePoint.x, edgePoint.y + innerOffset);
        }
        else if (edgeDirection == ConnectionDirection.East)
        {
            return new Vector2Int(edgePoint.x - innerOffset, edgePoint.y);
        }
        else if (edgeDirection == ConnectionDirection.West)
        {
            return new Vector2Int(edgePoint.x + innerOffset, edgePoint.y);
        }

        return edgePoint; // Fallback
    }

    /// <summary>
    /// Destruye la pared (GameObject con tag "Wall") en una posición local específica del chunk.
    /// </summary>
    /// <param name="point">La posición local (X,Z) dentro del chunk donde destruir la pared.</param>
    private void DestroyWallAt(Vector2Int point)
    {
        if (point.x == -1 || point.y == -1) return; // No destruir si el punto no es válido

        // Calcular la posición global del punto de la pared (asumiendo altura 1 para las paredes)
        Vector3 globalPoint = new Vector3(point.x, 1, point.y) + transform.position;

        // Usar OverlapBox para encontrar colliders en esa posición
        // El 0.4f es para que sea un poco más pequeño que el cubo y no solape con paredes adyacentes
        Collider[] walls = Physics.OverlapBox(
            globalPoint,
            Vector3.one * 0.4f
        );

        foreach (Collider wall in walls)
        {
            // Asegúrate de que tus prefabs de pared tengan el tag "Wall"
            if (wall.CompareTag("Wall"))
            {
                Destroy(wall.gameObject); // Eliminar el objeto de la pared
            }
        }
    }

    /// <summary>
    /// Obtiene una dirección de salida aleatoria que no sea la dirección de entrada.
    /// </summary>
    /// <param name="entryDir">La dirección de entrada a evitar.</param>
    /// <returns>Una dirección de salida válida o ConnectionDirection.None si no hay opciones.</returns>
    private ConnectionDirection GetRandomValidExitDirection(ConnectionDirection entryDir)
    {
        List<ConnectionDirection> possibleExits = new List<ConnectionDirection>
        {
            ConnectionDirection.North,
            ConnectionDirection.South,
            ConnectionDirection.East,
            ConnectionDirection.West
        };

        // No podemos salir por donde entramos
        possibleExits.Remove(entryDir);

        if (possibleExits.Count == 0) return ConnectionDirection.None;

        return possibleExits[Random.Range(0, possibleExits.Count)];
    }
}