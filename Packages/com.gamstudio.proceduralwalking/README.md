# GAM Procedural Walking

Procedural walking for rigged spider-like creatures (any number of legs, any number of bones per leg).
Move the body however you like; the legs find footholds, step in a configurable gait and the body
keeps its height and aligns itself to floors, slopes, walls and ceilings.

No dependencies besides the built-in Physics module. Works with any render pipeline.

## Install in another project

Pick one:

- **Git URL** – Package Manager > `+` > *Install package from git URL…*
  `https://github.com/Arnolddc04/GAM-ProceduralWalking.git?path=/Packages/com.gamstudio.proceduralwalking`
  (private repo: your git credentials must have access).
- **Local folder** – Package Manager > `+` > *Install package from disk…* and pick this folder's `package.json`.
- **Embedded** – copy the `com.gamstudio.proceduralwalking` folder into the other project's `Packages/` folder.

## Quick start

1. Select the creature's root object and use
   `GameObject > GAM Procedural Walking > Add Walker And Detect Legs`
   (or add a **Procedural Walker** component and press **Auto-Detect Legs**).
2. Move the root object from your own script in `Update`.

The walker runs in `LateUpdate`, after your movement and after any Animator.

## Components

| Component | Put it on | What it does |
| --- | --- | --- |
| **Procedural Walker** | the body / root that you move | Surface raycasts, gait, foot placement, body height and surface alignment, drives the IK. |
| **Procedural Leg** | a leg's first bone | Gait group, foot offset, home offset and step distance multiplier for that leg. Exposes the runtime state and a `Planted` event. |
| **IK Chain** | a leg's first bone (added with Procedural Leg) | FABRIK IK from Root to Tip with an optional pole. Also usable on its own with a Target transform. |
### Setting up legs

- **Auto-detect** finds bones that branch into two or more chains (at least 2 bones each) which are either
  named like legs (`leg`, `foot`, `femur`, `tibia`, `tarsus`, …) or reach downwards. Bones named like
  heads, tails, spines, antennae, abdomens, pedipalps etc. are skipped. Each leg follows the longest path to a leaf,
  so end bones (e.g. `_end` / tip bones) become the feet. Meshes, colliders, cameras and lights are never treated as bones.
- **Manually:** select a leg's first bone and its tip bone (two objects) and use
  `GameObject > GAM Procedural Walking > Create Leg From Selected Bones`, or add **Procedural Leg** to the first bone,
  set the **Tip** on its IK Chain and press **Collect From Children** on the walker.
- **Groups:** legs with the same group number step together and the groups take turns.
  **Alternate Groups** assigns a classic alternating gait (tripod for 6 legs, alternating tetrapod for 8).
  Use more groups (e.g. 4 groups of 2) for a slower wave gait, or switch **Gait** to *Independent*.
- **Auto-Configure** derives step distance, step height, cast lengths and the wall probe from the leg length.

### IK chain

- Any chain length. The bones are reset to the rest pose (captured when the chain initializes) before each solve,
  so the solve is stable and the rig's natural bend is preserved. Disable **Reset To Rest Pose** when an
  Animator poses the bones every frame.
- **Pole:** assign a transform to control the bend direction, or leave it empty for an automatic pole above the
  knee (pushed along the parent's up by **Auto Pole Up Bias**). **Bend Mode** chooses whether only the first
  joint (default) or all interior joints turn towards the pole.

### Joint limits

Every bone of a chain (except the tip) has a rotational limit, edited under **Joint Limits** in the IK Chain inspector:

| Type | Meaning |
| --- | --- |
| **Free** | No limit (default). |
| **Hinge** | Rotates only around **Axis** (bone-local), between **Angle Range** min and max. Knees, ankles. |
| **Ball** | Swings at most **Max Swing** degrees away from the rest direction (towards the child bone) and twists around that direction within **Twist Range**. Hips / coxae. |

- All angles are relative to the rest pose: 0° is the pose the rig had when the chain initialized.
- **Auto-Setup** makes the first bone a ball joint (swing 60°, twist ±60°) and every other bone a hinge (±70°) around the
  axis it bends on in the rest pose. **Detect Hinge Axes** recomputes only the axes. The walker's **Auto Joint Limits**
  button does this for every leg.
- When any joint is limited, the solver applies the limits bone by bone after FABRIK and then runs
  **Constraint Iterations** passes of constrained CCD to pull the tip back towards the target. A target outside the
  allowed range is reached as closely as the limits permit.
- With the chain (or one of its bones) selected, the Scene view shows each hinge's allowed arc (dotted line = rest,
  blue line = axis) and each ball joint's swing cone.
- A ball joint's twist is measured around the bone itself. If a hip bone points up and outwards, swinging the leg
  forward and back around the body's up axis counts partly as twist, so keep the twist range wide (±45° or more) for hips like that.

### How surfaces are found

For every leg, each frame, from its home position (the rest foot position, following the body):

1. **Wall check:** a ray from the body towards the foot. Hitting something means a wall is in the way (concave corner).
2. **Down:** a ray along the body's down axis from `Cast Height` above the home to `Cast Depth` below it.
3. **Edge wrap:** if nothing is below, a ray from below the home back under the body finds the side face of a ledge (convex corner).

The body up axis follows a blend of the hit normals and the plane through the footholds (**Feet Plane Weight**),
plus a **Wall Probe** along the movement direction that starts the climb before the body reaches a wall.
With no surface at all the body falls (optional) and turns back to world up.

The walker ignores its own colliders. Use **Ground Mask** to exclude other layers (e.g. other creatures).

## Gizmos

- Each leg and its bones are drawn in its group colour. Bones as octahedrons, joints as spheres, the root joint in white.
- **Edit mode:** home positions with the step radius, and the ground ray each leg will cast.
- **Play mode:** footholds with the step radius, feet (red = dangling), step paths and targets, the surface normal and the
  point the body height is measured from, and the wall probe.
- Change **Gizmo Display** on the walker to *When Selected* or *Never* to reduce clutter.

## Scripting

```csharp
var walker = GetComponent<ProceduralWalker>();
walker.LegPlanted += leg => PlayFootstep(leg.FootPosition, leg.FootNormal);

if (walker.IsGrounded) { Vector3 surfaceUp = walker.SurfaceNormal; }

// Rig changed at runtime:
walker.SetLegs(GetComponentsInChildren<ProceduralLeg>());

// Manual stepping (e.g. deterministic simulations): disable the component and call
walker.Tick(deltaTime);
```
