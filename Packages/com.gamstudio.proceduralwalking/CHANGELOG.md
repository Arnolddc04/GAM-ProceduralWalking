# Changelog

## [Unreleased]

### Removed
- Placeholder spider and demo environment builders, demo scene menu, `SimpleCrawlerController`, `CrawlerFollowCamera`,
  placeholder materials and the EditMode tests. The package now only contains the walking/IK runtime and the rig setup tools.
- Optional Input System reference from the runtime assembly.

## [0.2.0] - 2026-10-20

### Added
- Per-bone rotational limits on `IKChain` (`JointLimit`): Free, Hinge (axis + angle range) and Ball (swing cone + twist range),
  relative to the rest pose, enforced with constrained CCD after the FABRIK pass.
- Auto-setup of limits from the rest pose (ball root, hinges with detected bend axes), on the IK Chain and for all legs from the walker inspector.
- Scene gizmos for hinge arcs and ball cones. Placeholder spiders now come with joint limits.

## [0.1.0] - 2026-10-19

### Added
- `IKChain`: FABRIK IK constraint for chains of any length with pole / automatic bend hint and rest-pose reset.
- `ProceduralWalker` and `ProceduralLeg`: grouped or independent gaits, predictive foot placement, step arcs,
  settle-after-stop, body height and surface alignment with wall climbing, ceiling walking and ledge wrapping.
- `LegDetector`: automatic leg chain detection and alternating group assignment.
- Editor: walker and IK chain inspectors, scene gizmos, menu items for placeholder spider, demo environment and demo scene.
- `SimpleCrawlerController` and `CrawlerFollowCamera` for testing.
- EditMode tests.
