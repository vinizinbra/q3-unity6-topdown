Photon Quantum KCC Addon - Overview [WIP]

FEATURES
========================================
🟢 Control over position and look rotation (pitch + yaw). Works correctly with Transform3D.
🟢 Capsule based physics query and depenetration.
🟢 Support for Gravity.
🟢 Support for Jump.
🟢 Ground detection.
🟢 Continuous collision detection (CCD) out of the box.
🟢 Support for ignoring child colliders.
🟢 Step detection and ground snapping.
🟢 Platform independent, mobile friendly.
🟢 Performance optimized.
🟢 Dynamic (physics-like) and kinematic (unconstrained) velocity based movement.
🟢 Support for external forces (from other gameplay objects).
🟢 Pipeline driven by Processors for movement logic modularization.
🟢 Support for interactions - manually registered Modifier and Collision based.
🟢 Advanced collision and interaction filtering - callbacks, collider ignore list.
🟢 Collision enter/exit callbacks.
🟢 Base environment implementation with acceleration and friction models for movement.


FOLDER STRUCTURE
========================================
Assets/Photon/QuantumAddons/KCC
├───AssetDB    - Contains basic/minimal setup (KCC entity, KCC settings) and default KCC processor assets for movement (EnvironmentProcessor) and post-processing after move (GroundSnapProcessor, StepUpProcessor).
├───Example    - Contains simple game loop (Menu <=> Playground scene), player controller view from first-person and third-person perspective and other example gameplay elements related to character movement.
└───Simulation - Core KCC addon scripts.

Anything inside KCC/Example folder is supposed to be reused/modified/deleted.
Updating KCC addon means reimporting all assets in KCC/Simulation folder from new addon version, be careful when making changes there.


HOW IT WORKS?
========================================
1) Input processing.
	- Typically you set some properties based on your device input => KCC.SetInputDirection(), KCC.Jump(), KCC.AddExternalImpulse().
	- At this point the movement is not executed yet.
2) KCC System triggers udpate of all KCC components.
	- A) Initialization before move - Any KCC processor linked in KCC settings can implement IBeforeMove interface and convert input properties (from step 1) to KinematicVelocity and DynamicVelocity. These two are used to calculate position delta. Check EnvironmentProcessor.
	- B) CCD algorithm - The KCC moves step by step until the calculated position delta is fully consumed (the max delta in single step is 75% of radius). Each step involves depenetration from overlapping colliders and invocation of processor callbacks (IAfterMoveStep, OnEnter/OnExit callbacks).
	- C) Processing after move - Any KCC processor linked in KCC settings can implement IAfterMove interface and post-process movement (for example step detection and pushing character upwards in StepUpProcessor).


KCC PROCESSORS
========================================
What is the KCC processor?
- It's a single-purpose feature implementation which affects the KCC movement in a user-defined way.
- EnvironmentProcessor - defines movement while grounded/non-grounded, applies gravity, has some acceleration/friction parameters to simulate water/mud/ice.
- StepUpProcessor - checks if the character is blocked by an obstacle and tries to project the un-applied position delta to upwards movement if there is a step in front of the character.
- GroundSnapProcessor - checks surface below the character after losing grounded state (going over an edge) and snaps the KCC.

How the KCC interacts with processors?
1) Static list defined in KCC settings asset. These processors are executed always. Good for default movement behavior and always-on features.
2) Collision with a static collider. The KCCProcessor asset must be linked in Quantum static collider component => Asset field.
3) Collision with an entity collider. The entity must have a KCCProcessorLink component with KCCProcessor asset linked in Processor field.
4) Manually registered processor (Modifier). KCC.AddModifier(), KCC.RemoveModifier().

Processor callbacks (please check source code for detailed explanation and usage):
- KCCProcessor.OnEnter()
- KCCProcessor.OnExit()
- IBeforeMove.BeforeMove()
- IAfterMoveStep.AfterMoveStep()
- IAfterMove.AfterMove()

IMPORTANT NOTES
========================================
- CS0234: The type or namespace 'InputSystem' does not exist in the namespace 'UnityEngine' => Navigate to Assets/Photon/Quantum/Runtime/Quantum.Unity.asmdef and add a reference Unity.InputSystem.dll.
