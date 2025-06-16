// PhysicsCube.cs
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(Rigidbody))]
public class PhysicsCube : MonoBehaviour
{
    private Rigidbody rb;
    private XRGrabInteractable grabInteractable;
    private bool physicsEnabled = true; // Estado actual de las físicas

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        grabInteractable = GetComponent<XRGrabInteractable>();

        // Configuración inicial del Rigidbody (puedes ajustarla)
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // Configuración del XRGrabInteractable (ajusta según tu sistema de agarre)
        if (grabInteractable != null)
        {
            // Puedes configurar eventos aquí si es necesario
            // grabInteractable.selectEntered.AddListener(OnGrabbed);
            // grabInteractable.selectExited.AddListener(OnReleased);
        }
    }

    // Método público para alternar las físicas (llamado desde UIManager)
    public void TogglePhysics()
    {
        physicsEnabled = !physicsEnabled;

        if (rb != null)
        {
            rb.isKinematic = !physicsEnabled; // Si physicsEnabled es true, isKinematic es false (con físicas)
            // Si physicsEnabled es false, isKinematic es true (sin físicas, "snap")
            rb.useGravity = physicsEnabled;   // Usar gravedad solo si las físicas están activadas

            // Si se desactiva las físicas mientras se está moviendo, parar el cubo
            if (!physicsEnabled)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                Debug.Log("Físicas del cubo DESACTIVADAS (Snap).");
            }
            else
            {
                Debug.Log("Físicas del cubo ACTIVADAS.");
            }
        }
    }
}