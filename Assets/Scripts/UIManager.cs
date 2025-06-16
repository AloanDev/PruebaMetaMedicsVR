using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

public class UIManager : MonoBehaviour
{
    public GameObject uiPanel;
    public GameObject cuboPrefab;
    public Transform playerCameraTransform;

    public InputActionReference botonAbrirPanel;

    private GameObject currentCuboInstance;
    private bool isUIPanelActive = false;

    void OnEnable()
    {
        if (botonAbrirPanel != null && botonAbrirPanel.action != null)
        {
            botonAbrirPanel.action.performed += OnAbrirPanelPressed;
            botonAbrirPanel.action.Enable();
        }
        else
        {
            Debug.LogWarning("La acción para abrir el panel (botonAbrirPanel) no está asignada o no es válida en UIManager. Asegúrate de configurarla en el Inspector.");
        }
    }

    void OnDisable()
    {
        if (botonAbrirPanel != null && botonAbrirPanel.action != null)
        {
            botonAbrirPanel.action.performed -= OnAbrirPanelPressed;
            botonAbrirPanel.action.Disable();
        }
    }

    void Start()
    {
        if (uiPanel != null)
        {
            uiPanel.SetActive(false);
        }
        else
        {
            Debug.LogError("Panel UI no asignado en UIManager. Asigna tu panel en el Inspector.");
        }

        if (playerCameraTransform == null)
        {
            Debug.LogError("Player Camera Transform no asignado en UIManager. Arrastra la cámara principal del jugador al Inspector.");
        }

        if (cuboPrefab == null)
        {
            Debug.LogError("Cubo Prefab no asignado en UIManager. Arrastra el prefab de tu cubo al Inspector.");
        }
    }

    private void OnAbrirPanelPressed(InputAction.CallbackContext context)
    {
        if (uiPanel != null)
        {
            isUIPanelActive = !isUIPanelActive;
            uiPanel.SetActive(isUIPanelActive);
            Debug.Log("Panel UI " + (isUIPanelActive ? "activado" : "desactivado") + " con el botón A.");
        }
    }

    public void InstanciarCubo()
    {
        if (cuboPrefab == null)
        {
            Debug.LogError("No hay un prefab de cubo asignado en el UIManager.");
            return;
        }
        if (playerCameraTransform == null)
        {
            Debug.LogError("No hay Player Camera Transform asignado para instanciar el cubo.");
            return;
        }

        if (currentCuboInstance != null)
        {
            Destroy(currentCuboInstance);
        }

        Vector3 spawnPosition = playerCameraTransform.position + playerCameraTransform.forward  + playerCameraTransform.up ;
        Quaternion spawnRotation = Quaternion.identity;

        currentCuboInstance = Instantiate(cuboPrefab, spawnPosition, spawnRotation);
        Debug.Log("Cubo instanciado en: " + spawnPosition);

        // Ensure the cube has the PhysicsCube script if it doesn't already
        if (currentCuboInstance.GetComponent<PhysicsCube>() == null)
        {
            currentCuboInstance.AddComponent<PhysicsCube>();
        }
    }

    public void ResetearCubo()
    {
        if (currentCuboInstance == null)
        {
            Debug.LogWarning("No hay un cubo instanciado para resetear.");
            return;
        }
        if (playerCameraTransform == null)
        {
            Debug.LogError("No hay Player Camera Transform asignado para resetear el cubo.");
            return;
        }

        Vector3 resetPosition = playerCameraTransform.position + playerCameraTransform.forward * 1f + playerCameraTransform.up * 1f;
        currentCuboInstance.transform.position = resetPosition;
        currentCuboInstance.transform.rotation = Quaternion.identity;

        Rigidbody rb = currentCuboInstance.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        Debug.Log("Cubo reseteado a la posición: " + resetPosition);
    }

    public void AlternarFisicasCubo()
    {
        if (currentCuboInstance == null)
        {
            Debug.LogWarning("No hay un cubo instanciado para alternar físicas.");
            return;
        }

        PhysicsCube cuboScript = currentCuboInstance.GetComponent<PhysicsCube>();
        if (cuboScript != null)
        {
            cuboScript.TogglePhysics();
        }
        else
        {
            Debug.LogError("El cubo instanciado no tiene el script 'PhysicsCube'.");
        }
    }
}