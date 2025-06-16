# PruebaMetaMedicsVR

--------------------------------------------------------------------------------
PROJECT README - VR HAND TRACKING & PROCEDURAL LEVEL GENERATION
--------------------------------------------------------------------------------

This Unity project showcases a combination of procedural level generation, VR hand tracking interactions, and a debug/utility panel for object manipulation.

--------------------------------------------------------------------------------
1. INSTALLATION & SETUP INSTRUCTIONS
--------------------------------------------------------------------------------

1.  Unity Version: Use Unity Hub to open the project with Unity 2022.3 LTS (or newer). Ensure "Android Build Support" module is installed.

2.  Android Build Configuration:
    -   File > Build Settings: Switch platform to Android.
    -   Edit > Project Settings > Player > Android tab > Other Settings > Compression Method: Set to ASTC.

3.  XR Plugin Management:
    -   Edit > Project Settings > XR Plugin Management > Android Tab: Check "Oculus".

4.  Meta XR SDK (Oculus Integration):
    -   Download and Import Meta XR SDK (formerly "Oculus Integration") from the Unity Asset Store or Package Manager.
    -   Import all essential VR and Interaction components, especially Oculus > Interaction > Runtime and Oculus > SampleFramework > Usage > Hands.
    -   Accept all prompts for project upgrades/restarts after import.

5.  Scene Setup:
    -   Ensure your main scene contains the _GameManager (or equivalent) with the LevelGeneratorManager script.
    -   Verify OVRCameraRig is in the scene (replaces Main Camera).
    -   Confirm OVRHandPrefab (Left) and OVRHandPrefab (Right) are children of their respective HandAnchors under OVRCameraRig > TrackingSpace.
    -   Assign playerReference in LevelGeneratorManager's Inspector.

--------------------------------------------------------------------------------
2. SYSTEM USAGE GUIDE
--------------------------------------------------------------------------------

* Procedural Level Generation:
    The LevelGeneratorManager script dynamically generates terrain chunks around the player in a seamless fashion.
    The player starts on a generated path in the central chunk.

* VR Hand Tracking Interaction:
    Main Interaction: Utilizes Meta Quest 2's native hand tracking. Your real-world hand movements are mirrored by virtual hands.
    Gesture Activation: An InteractiveCube is placed in the scene. Performing a Pinch gesture with either hand on this cube will toggle its color, demonstrating basic gesture recognition.

* Debug/Utility Panel (New!):
    Press the "A" button on your right Meta Quest controller to toggle the visibility of a debug panel.
    This panel provides:
    -   Instantiate Cube Button: Spawns a new cube in front of the player.
    -   Reset Cube Position Button: Resets the spawned cube's position.
    -   Toggle Physics Button: Toggles the Rigidbody component (and thus physics simulation) on the spawned cube.

--------------------------------------------------------------------------------
3. TECHNICAL DECISIONS EXPLAINED
--------------------------------------------------------------------------------

* Centralized Level Generation: LevelGeneratorManager handles all terrain generation using object pooling and a dictionary to manage chunk data. This optimizes performance by reusing chunks and loading/unloading based on player proximity.

* Pre-generated Terrain Heights: Terrain heights are calculated once per chunk by LevelGeneratorManager using Perlin noise and passed to OrganicChunkGenerator. This ensures consistent terrain across re-activated chunks and allows for global noise parameters.

* Oculus Integration & XR Interaction Toolkit: Chosen for robust, optimized hand tracking and interaction capabilities specific to Meta Quest devices. It provides a reliable framework for gesture detection and interaction.

* Simple Gesture Detection: For the InteractiveCube, direct event subscriptions (WhenHandPinchStarted) are used for clarity, while Oculus.Interaction's HandGrabUse and HandPinchUse components handle the underlying gesture recognition.

* Debug Panel: A standard Unity UI Canvas in World Space is used, activated by a controller button. This provides immediate in-VR feedback and debugging tools without requiring a PC connection.

--------------------------------------------------------------------------------
4. COMMON TROUBLESHOOTING
--------------------------------------------------------------------------------

* No Hands in VR:
    -   Ensure your Meta Quest 2 is in Developer Mode.
    -   Check Project Settings > XR Plugin Management > Android for "Oculus" checkbox.
    -   Verify OVRCameraRig > OVRManager > Hand Tracking Support is set to Controllers And Hands or Hands Only and Hand Tracking Version is V2.
    -   Ensure OVRHandPrefab (Left/Right) are correctly parented under their respective HandAnchors.

* Object Not Reacting to Gestures:
    -   Verify the MyInteractiveObject script is attached to the target object.
    -   Double-check that the HandGrabUse and HandPinchUse components from the OVRHandPrefabs are correctly assigned to the script's Inspector slots.
    -   Confirm the Activation Gesture setting in the Inspector matches the gesture you are performing.
    -   Check Unity's Console for Debug.LogError messages from MyInteractiveObject.

* Debug Panel Not Appearing/Working:
    -   Ensure the script responsible for the debug panel is active and the "A" button input is correctly registered.
    -   Verify the UI elements in the panel are correctly set up in World Space Canvas.

* Performance Issues:
    -   Reduce maxActiveChunksWindow in LevelGeneratorManager for lower-end devices.
    -   Optimize models (trees, blocks) for VR.
    -   Ensure only necessary Meta XR SDK components were imported.

--------------------------------------------------------------------------------