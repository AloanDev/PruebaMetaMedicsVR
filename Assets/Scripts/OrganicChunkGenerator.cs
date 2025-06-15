using UnityEngine;
using System.Collections.Generic;
using System.Linq;

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
    /// Genera el contenido del chunk basándose en una semilla, dirección de entrada y salidas deseadas,
    /// y los datos de altura del terreno.
    /// </summary>
    /// <param name="seed">La semilla para la generación de este chunk.</param>
    /// <param name="entryDir">La dirección por la que el jugador entró o se conectará a este chunk.</param>
    /// <param name="desiredExits">Una lista de direcciones por las que se desea que el chunk tenga salidas.</param>
    /// <param name="terrainHeightsData">Los datos de altura [x,z] para cada columna de terreno en este chunk.</param>
    /// <param name="dirtDepth">La profundidad de la capa de tierra debajo del césped (no usada para montañas de momento).</param>
    public void GenerateChunk(int seed, ConnectionDirection entryDir, List<ConnectionDirection> desiredExits,
        int[,] terrainHeightsData, int dirtDepth)
    {
        m_Seed = seed;
        Random.InitState(m_Seed);

        Debug.Log(
            $"Generando chunk en {transform.position} con semilla: {m_Seed}, Entrada: {entryDir}, Salidas deseadas: {string.Join(", ", desiredExits)}");

        // Limpiar objetos hijos existentes (útil para la reutilización del pool)
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }

        EntryDirection = entryDir;
        EntryPoint = GetEdgePoint(entryDir);

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

        // --- NUEVO: CALCULAR EL CAMINO COMPLETO ANTES DE INSTANCIAR LOS BLOQUES ---
        // Usamos HashSet para almacenar los puntos del camino para búsquedas eficientes.
        HashSet<Vector2Int> pathPoints = new HashSet<Vector2Int>();

        // Añadir puntos de entrada y sus internos al conjunto del camino
        if (EntryPoint.x != -1) pathPoints.Add(EntryPoint);
        pathPoints.Add(internalPathStart);

        // Generar caminos para cada salida deseada (simulando, solo colectando puntos)
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

            List<Vector2Int> segmentPath = new List<Vector2Int>(); // Lista temporal para los puntos de este segmento
            Vector2Int
                midPoint = GetMidPointForForcedTurn(internalPathStart,
                    currentInternalExit); // Intenta encontrar un punto intermedio para forzar un giro en L

            if (midPoint.x != -1) // Si se encontró un midPoint válido
            {
                TraversePathSegment_Simulate(internalPathStart, midPoint,
                    segmentPath); // Primero segmento: inicio a punto intermedio
                Vector2Int current = segmentPath.Any() ? segmentPath.Last() : internalPathStart;
                if (current != currentInternalExit)
                {
                    TraversePathSegment_Simulate(current, currentInternalExit,
                        segmentPath); // Segundo segmento: punto intermedio a salida
                }
            }
            else // Si no hay midPoint, intenta un camino directo
            {
                TraversePathSegment_Simulate(internalPathStart, currentInternalExit, segmentPath);
            }

            // Asegurarse de que el camino realmente llegó al punto de salida
            Vector2Int finalCurrent = segmentPath.Any() ? segmentPath.Last() : internalPathStart;
            if (finalCurrent != currentInternalExit)
            {
                //Debug.LogWarning($"Chunk {name}: El camino a {exitDir} no llegó al final, realizando limpieza final (simulada).");
                ForceCompletePath_Simulate(finalCurrent, currentInternalExit, segmentPath);
            }

            // Añadir todos los puntos de este segmento al conjunto global del camino
            foreach (var p in segmentPath) pathPoints.Add(p);
            pathPoints.Add(currentExitPoint); // Asegurarse de que el punto de salida esté incluido

            // Añadir a las listas de salidas activas del chunk
            ActiveExitPoints.Add(currentExitPoint);
            ActiveExitDirections.Add(exitDir);
        }

        // --- NUEVO: Instanciar los bloques, pasando los puntos del camino ---
        InstantiateChunkBlocks(terrainHeightsData, pathPoints);
    }

    /// <summary>
    /// Instancia los bloques del chunk (suelo, paredes y montañas) basándose en los datos de altura
    /// y los puntos que forman el camino. NO destruye nada, solo construye.
    /// </summary>
    /// <param name="terrainHeightsData">Los datos de altura de Perlin noise.</param>
    /// <param name="pathPoints">Un HashSet de Vector2Int que contiene todos los puntos (X,Z) del camino.</param>
    private void InstantiateChunkBlocks(int[,] terrainHeightsData, HashSet<Vector2Int> pathPoints)
    {
        for (int x = 0; x < chunkSize; x++)
        {
            for (int z = 0; z < chunkSize; z++)
            {
                Vector2Int currentPoint = new Vector2Int(x, z);

                // Siempre instanciar el suelo en Y=0
                Instantiate(floorPrefab, new Vector3(x, 0, z) + transform.position, Quaternion.identity, transform);

                // Lógica para instanciar la primera capa de "pared" (Y=1) y las montañas (Y>=2)
                // SOLO si el punto NO es parte del camino
                if (!pathPoints.Contains(currentPoint))
                {
                    int heightFromPerlin = terrainHeightsData[x, z]; // Altura total (baseTerrainHeight + elevación)

                    // Instanciar la primera capa de "pared" en Y=1.
                    // Esto se hace si heightFromPerlin es 1 (terreno plano) o mayor que 1 (montaña).
                    // Siempre que no sea un punto de camino, tendrá pared en Y=1.
                    // NO aplicamos el margen de montaña a la capa Y=1, porque es parte del "suelo de pared" alrededor del camino.
                    Instantiate(wallPrefab, new Vector3(x, 1, z) + transform.position, Quaternion.identity, transform);

                    // Generación de montañas/terreno 3D (a partir de Y=2)
                    // Si la altura generada es mayor que la capa base de pared (Y=1)
                    if (heightFromPerlin > 1)
                    {
                        // --- NUEVA VERIFICACIÓN PARA EL MARGEN DE MONTAÑA ---
                        // Solo construimos bloques de montaña (Y>=2) si no están adyacentes al camino.
                        if (!IsAdjacentToPath(currentPoint, pathPoints)) // <--- Nueva condición
                        {
                            for (int y = 2; y <= heightFromPerlin; y++)
                            {
                                Instantiate(wallPrefab, new Vector3(x, y, z) + transform.position, Quaternion.identity,
                                    transform);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Verifica si un punto (x,z) está adyacente (ortogonalmente) a cualquier punto del camino.
    /// </summary>
    /// <param name="point">El punto a verificar.</param>
    /// <param name="pathPoints">El HashSet de puntos que forman el camino.</param>
    /// <returns>True si el punto es adyacente a un punto del camino, False en caso contrario.</returns>
    private bool IsAdjacentToPath(Vector2Int point, HashSet<Vector2Int> pathPoints)
    {
        Vector2Int[] orthogonalDirections = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

        foreach (var dir in orthogonalDirections)
        {
            Vector2Int adjacentPoint = point + dir;
            if (pathPoints.Contains(adjacentPoint))
            {
                return true; // Se encontró un punto del camino adyacente
            }
        }

        return false; // No se encontraron puntos del camino adyacentes
    }

    /// <summary>
    /// Traza un segmento de camino entre dos puntos, evitando caminos duplicados.
    /// Esta versión es para SIMULACIÓN, solo colecta los puntos, no destruye GameObjects.
    /// </summary>
    /// <param name="segmentStart">Punto de inicio del segmento (local).</param>
    /// <param name="segmentEnd">Punto final del segmento (local).</param>
    /// <param name="path">Lista que almacena los puntos del camino generados.</param>
    private void TraversePathSegment_Simulate(Vector2Int segmentStart, Vector2Int segmentEnd, List<Vector2Int> path)
    {
        Vector2Int currentSegmentPoint = segmentStart;
        int attempts = 0;

        if (!path.Contains(segmentStart))
        {
            path.Add(segmentStart);
        }

        while (currentSegmentPoint != segmentEnd && attempts < MMaxAttempts)
        {
            Vector2Int next = GetNextStep(currentSegmentPoint, segmentEnd, path);

            if (next == currentSegmentPoint)
            {
                // Si está atascado, forzar el camino y terminar este segmento.
                ForceCompletePath_Simulate(currentSegmentPoint, segmentEnd, path);
                currentSegmentPoint = segmentEnd;
                break;
            }

            path.Add(next);
            currentSegmentPoint = next;
            attempts++;
        }

        // Si el camino no llegó al final, forzar el completado como último recurso.
        if (currentSegmentPoint != segmentEnd)
        {
            ForceCompletePath_Simulate(currentSegmentPoint, segmentEnd, path);
        }
    }

    /// <summary>
    /// Determina el siguiente paso en el camino hacia un punto final.
    /// Intenta moverse hacia el objetivo con preferencia, o aleatoriamente si no es posible.
    /// Garantiza que el camino se mantenga con un cubo de ancho.
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
            if (IsPathPointInBounds(potentialStep) && !path.Contains(potentialStep))
            {
                if (CountAdjacentPathPoints(potentialStep, path) <= 2)
                {
                    if (Random.value > randomness)
                    {
                        return potentialStep;
                    }
                }
            }
        }

        List<Vector2Int> directionsToTry = new List<Vector2Int>(orthogonalDirections);
        directionsToTry = directionsToTry.OrderBy(x => Random.value).ToList();

        foreach (var dir in directionsToTry)
        {
            Vector2Int step = current + dir;
            if (IsPathPointInBounds(step) && !path.Contains(step))
            {
                if (CountAdjacentPathPoints(step, path) <= 2)
                {
                    validSteps.Add(step);
                }
            }
        }

        if (validSteps.Count > 0)
        {
            return validSteps[Random.Range(0, validSteps.Count)];
        }

        return current;
    }

    /// <summary>
    /// Cuenta el número de puntos adyacentes (ortogonales) a un punto dado que ya están en el camino.
    /// </summary>
    /// <param name="point">El punto para el que se comprobarán los adyacentes.</param>
    /// <param name="path">La lista de puntos que ya forman el camino.</param>
    /// <returns>El número de puntos adyacentes al 'point' que están en 'path'.</returns>
    private int CountAdjacentPathPoints(Vector2Int point, List<Vector2Int> path)
    {
        int count = 0;
        Vector2Int[] orthogonalDirections = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

        foreach (var dir in orthogonalDirections)
        {
            Vector2Int adjacentPoint = point + dir;
            if (path.Contains(adjacentPoint))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Fuerza la finalización de un camino conectando directamente los puntos.
    /// Utilizado como fallback si el algoritmo orgánico se atasca.
    /// Esta versión es para SIMULACIÓN, solo colecta los puntos, no destruye GameObjects.
    /// </summary>
    /// <param name="from">Punto de inicio actual (local).</param>
    /// <param name="to">Punto final objetivo (local).</param>
    /// <param name="path">Lista de puntos del camino a la que añadir.</param>
    private void ForceCompletePath_Simulate(Vector2Int from, Vector2Int to, List<Vector2Int> path)
    {
        Vector2Int current = from;

        while (current != to)
        {
            Vector2Int next = current;

            if (current.x != to.x)
            {
                next.x += (to.x > current.x) ? 1 : -1;
            }
            else if (current.y != to.y)
            {
                next.y += (to.y > current.y) ? 1 : -1;
            }

            if (next == current) break; // Atascado o ya llegó

            if (IsPathPointInBounds(next) && !path.Contains(next))
            {
                path.Add(next);
            }
            else if (next == to) // Si el siguiente punto es el destino y aún no está en el camino
            {
                if (!path.Contains(next))
                {
                    path.Add(next);
                }

                current = to; // Asegura que el bucle termine
                break;
            }
            else
            {
                // Esto podría ocurrir si 'next' está fuera de límites y no es 'to', o ya está en el camino
                // Y no podemos añadirlo. En un force, esto es un último recurso, así que nos rendimos.
                break;
            }

            current = next;
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
        int minInnerBound = 1;
        int maxInnerBound = chunkSize - 2;

        List<Vector2Int> potentialMidPoints = new List<Vector2Int>();

        Vector2Int candidate1 = new Vector2Int(start.x, end.y);
        Vector2Int candidate2 = new Vector2Int(end.x, start.y);

        if (start.x != end.x && start.y != end.y)
        {
            if (IsPathPointInBounds(candidate1) && candidate1 != start && candidate1 != end)
                potentialMidPoints.Add(candidate1);
            if (IsPathPointInBounds(candidate2) && candidate2 != start && candidate2 != end)
                potentialMidPoints.Add(candidate2);
        }
        else
        {
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

        if (potentialMidPoints.Any())
        {
            return potentialMidPoints[Random.Range(0, potentialMidPoints.Count)];
        }

        for (int i = 0; i < 100; i++)
        {
            int randX = Random.Range(minInnerBound, maxInnerBound + 1);
            int randY = Random.Range(minInnerBound, maxInnerBound + 1);
            Vector2Int randomMid = new Vector2Int(randX, randY);

            if (randomMid != start && randomMid != end &&
                !IsCollinear(start, end, randomMid))
            {
                return randomMid;
            }
        }

        Debug.LogWarning(
            $"No se pudo encontrar un midPoint de giro válido para start:{start} y end:{end} en chunk {transform.position}. Generando camino recto como fallback.");
        return new Vector2Int(-1, -1);
    }

    /// <summary>
    /// Comprueba si tres puntos son colineales (están en la misma línea horizontal o vertical).
    /// </summary>
    private bool IsCollinear(Vector2Int p1, Vector2Int p2, Vector2Int p3)
    {
        return (p1.x == p2.x && p3.x == p1.x) || (p1.y == p2.y && p3.y == p1.y);
    }

    /// <summary>
    /// Comprueba si un punto dado está dentro de los límites internos del chunk.
    /// </summary>
    private bool IsPathPointInBounds(Vector2Int point)
    {
        int minBound = 1;
        int maxBound = chunkSize - 2;

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
            case ConnectionDirection.None: return new Vector2Int(-1, -1);
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
        int innerOffset = 1;

        if (edgeDirection == ConnectionDirection.Center || edgePoint.x == -1)
        {
            return edgePoint;
        }

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

        return edgePoint;
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

        possibleExits.Remove(entryDir);

        if (possibleExits.Count == 0) return ConnectionDirection.None;

        return possibleExits[Random.Range(0, possibleExits.Count)];
    }
}