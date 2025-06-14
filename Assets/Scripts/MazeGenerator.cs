using UnityEngine;
using System.Collections.Generic;

public class MazeGenerator : MonoBehaviour
{
    [Header("Settings")] public int chunkSize = 13;
    private int m_Seed;
    [Range(0.1f, 0.9f)] public float randomness = 0.3f;
    private const int MMaxAttempts = 1000; // Límite de seguridad

    [Header("Prefabs")] public GameObject floorPrefab;
    public GameObject wallPrefab;

    private void Start()
    {
        GenerateOrganicChunk();
    }

    private void GenerateOrganicChunk()
    {
        m_Seed = Random.Range(int.MinValue, int.MaxValue); // Semilla aleatoria
        Random.InitState(m_Seed);
        
        Debug.Log($"Generando chunk con semilla: {m_Seed}"); // Opcional: ver la semilla usada

        GenerateBase();
        
        int startSide = Random.Range(0, 4);
        Vector2Int start = GetEdgePoint(startSide);

        List<int> availableSides = new List<int> { 0, 1, 2, 3 };
        availableSides.Remove(startSide);
        Vector2Int end = GetEdgePoint(availableSides[Random.Range(0, 3)]);

        CreateOrganicPath(start, end);
    }

    private void GenerateBase()
    {
        for (int x = 0; x < chunkSize; x++)
        {
            for (int z = 0; z < chunkSize; z++)
            {
                Instantiate(floorPrefab, new Vector3(x, 0, z), Quaternion.identity, transform);
                Instantiate(wallPrefab, new Vector3(x, 1, z), Quaternion.identity, transform);
            }
        }
    }

    private void CreateOrganicPath(Vector2Int start, Vector2Int end)
    {
        List<Vector2Int> path = new List<Vector2Int>();
        Vector2Int current = start;
        path.Add(current);
        DestroyWallAt(current);

        int attempts = 0;
        bool useEmergencyRoute = false;

        while (current != end && attempts < MMaxAttempts)
        {
            Vector2Int next;

            if (!useEmergencyRoute)
            {
                // Modo normal (orgánico)
                next = GetNextStep(current, end, path);

                // Si está atascado, activa ruta de emergencia
                if (path.Contains(next))
                {
                    useEmergencyRoute = true;
                    continue;
                }
            }
            else
            {
                // Modo emergencia (camino recto al objetivo)
                next = GetEmergencyStep(current, end);
            }

            path.Add(next);
            current = next;
            DestroyWallAt(current);
            attempts++;
        }

        if (attempts >= MMaxAttempts)
        {
            Debug.LogWarning("Ruta de emergencia activada");
            ForceCompletePath(current, end, path);
        }
    }

    private Vector2Int GetNextStep(Vector2Int current, Vector2Int end, List<Vector2Int> path)
    {
        // 70% probabilidad de moverse hacia el final
        if (Random.value > randomness)
        {
            Vector2Int direction = new Vector2Int(
                Mathf.Clamp(end.x - current.x, -1, 1),
                Mathf.Clamp(end.y - current.y, -1, 1)
            );

            // Prioriza dirección no diagonal
            if (direction.x != 0 && direction.y != 0)
            {
                if (Random.value > 0.5f) direction.y = 0;
                else direction.x = 0;
            }

            Vector2Int next = current + direction;
            if (IsInBounds(next)) return next;
        }

        // Movimiento aleatorio seguro
        List<Vector2Int> possibleSteps = new List<Vector2Int>();
        Vector2Int[] directions = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

        foreach (var dir in directions)
        {
            Vector2Int step = current + dir;
            if (IsInBounds(step)) possibleSteps.Add(step);
        }

        return possibleSteps.Count > 0 ? possibleSteps[Random.Range(0, possibleSteps.Count)] : current;
    }

    private Vector2Int GetEmergencyStep(Vector2Int current, Vector2Int end)
    {
        // Camino recto ignorando todo
        Vector2Int step = current;

        if (step.x != end.x)
            step.x += (end.x > current.x) ? 1 : -1;
        else if (step.y != end.y)
            step.y += (end.y > current.y) ? 1 : -1;

        return step;
    }

    private void ForceCompletePath(Vector2Int from, Vector2Int to, List<Vector2Int> path)
    {
        Vector2Int current = from;

        while (current != to)
        {
            current = GetEmergencyStep(current, to);
            if (!path.Contains(current))
            {
                path.Add(current);
                DestroyWallAt(current);
            }
        }
    }

    private bool IsInBounds(Vector2Int point)
    {
        return point.x >= 0 && point.x < chunkSize && point.y >= 0 && point.y < chunkSize;
    }

    private Vector2Int GetEdgePoint(int side)
    {
        return side switch
        {
            0 => new Vector2Int(chunkSize / 2, 0),
            1 => new Vector2Int(chunkSize / 2, chunkSize - 1),
            2 => new Vector2Int(chunkSize - 1, chunkSize / 2),
            _ => new Vector2Int(0, chunkSize / 2)
        };
    }

    private void DestroyWallAt(Vector2Int point)
    {
        Collider[] walls = Physics.OverlapBox(
            new Vector3(point.x, 1, point.y),
            Vector3.one * 0.4f
        );

        foreach (Collider wall in walls)
        {
            if (wall.CompareTag("Wall"))
                Destroy(wall.gameObject);
        }
    }
}