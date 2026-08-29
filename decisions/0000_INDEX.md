# Architecture decisions — 2026-08-29

1. Approved Windows WPF/.NET 8 application, following RoboStation organization and theme conventions.
2. C++ image-processing core through an owned-buffer C ABI, with .NET responsible for application/UI.
3. One camera, two sequential captures, distinct expected text per end, ordinal exact comparison.
4. One search ROI per end; automatic detection returns the ordered text regions. No fixed two-sub-ROI model.
5. Shared ImageViewer viewport with read-only overlays; ImageEditor adds geometry editing only.
6. Recipes are explicit operator-selected models, not 160 separate OCR networks.
7. Color/template/print-quality classification and external PLC trigger wiring are deferred.
8. Missing OCR/vendor assets must remain visible blockers. Do not replace them with mock production implementations.
9. Source reference repositories are read-only. Adapt reusable resources into this repository with provenance.
10. No hardware or OCR accuracy acceptance is claimed by software build/tests/UI fixtures.
11. User's HUD correction: ImageHud is mandatory for all image surfaces, matching RoboStation Geo Measure's translucent overlay containers, vector icons and selected-tool states. Live moves into the acquisition column to prioritize editor height. See docs/design/DESIGN_SYSTEM.md.
