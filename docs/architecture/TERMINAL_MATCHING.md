# Terminal template matching

Implemented after approval on 2026-09-04. No OCR decoder, expected-text conditioning, camera trigger or PLC output algorithm was changed.

## Acceptance contract

For each enabled end: `OK = exact ordered text AND required text direction AND terminal template match`. Product OK requires both ends OK. OCR and matching inspect the same owned frame; there is no second grab, cross-end search, algorithm fallback or threshold auto-relaxation. A matching failure is NG; invalid assets/native failures fault the cycle and cannot emit an OK result. Simulator bypasses hardware only, not these acceptance rules.

Text direction is still an independently observed 0°/180° value. Matching angle is the **relative pose of the terminal against its taught image**, in image coordinates (clockwise positive); these are not interchangeable.

## Teaching workflow

1. Log in as Admin, create/select the model, load/grab the reference image of each end and configure its existing OCR ROI/text/direction.
2. Open **Terminal template** under that end's text fields. Use the current reference image or load a separate image. Draw a rectangle, circle or polygon around the distinctive terminal geometry. Avoid relying only on the printed marker text when the intention is to distinguish the terminal itself.
3. On the right, draw the runtime search ROI in full source-image coordinates. Loading a test frame never teaches it as the template. Test frames must have the same dimensions as the recipe reference. Learning ROI and search ROI are separate from the OCR ROI.
4. Select Normal, AKAZE, SIFT, ORB or ORB Max Stable. Basic thresholds and advanced parameters are saved separately for each algorithm; switching algorithms does not discard the previous profile. Changing language only updates stable labels.
5. Test on good and bad samples. Inspect score, NCC, SSIM, edge agreement, pose, inlier count/ratio, feature coverage and valid-pixel coverage. The two lower viewers show the learned crop and the aligned candidate. No candidate is a rejection, not a request to silently use another algorithm.
6. Apply the isolated template draft, Apply the end, then Save the complete Recipe. Cancel leaves the recipe unchanged. Operator cannot open/change teaching controls. New models require template teaching on both ends by default. Admin can explicitly disable an end's template check; the editor and RUN result then say **OCR ONLY**. Legacy v1/v2 recipes remain OCR-only until explicitly taught, not silently enabled.

## Algorithms and gates

All algorithms use grayscale images; optional Gaussian/CLAHE affects candidate discovery only. Final appearance verification uses the original grayscale pixels.

- **Normal**: masked normalized correlation (0 = CCOEFF_NORMED; 1 = CCORR_NORMED; 2 = SQDIFF_NORMED). Coarse angle/scale sweep, fine sweep around the strongest candidate, spatial suppression and verification of up to eight distinct candidate positions. Always full resolution. Search-mask coverage excludes peaks outside circular/polygonal search areas.
- **AKAZE**: MLDB descriptors, Hamming KNN ratio/distance gates and mutual best matches.
- **SIFT**: SIFT descriptors, L2 KNN ratio/distance gates and mutual best matches.
- **ORB**: ORB descriptors, Hamming KNN ratio/distance gates and mutual best matches.
- **ORB Max Stable**: ORB with default Gaussian 3, CLAHE clip 3 and 3000 keypoints, followed by the same strict geometric/appearance checks. This is not a claim that ORB must beat other algorithms on every terminal.

Feature modes use RANSAC homography with minimum matches, inliers, inlier ratio and convex-hull feature coverage. A second search excludes the first candidate's footprint to detect a competing instance. Insufficient descriptors, matches or inliers now produce specific reason codes and retain counts rather than a zero-filled NoCandidate. A small/smooth terminal may need Normal or a larger distinctive learn ROI.

Every candidate must satisfy convex non-reflected geometry, configured angle and both axis-scale bounds, limited skew/aspect/perspective distortion, and sufficient valid source-mask coverage. Warping uses constant borders; fabricated repeated border pixels cannot contribute to confidence.

Appearance gates are masked zero-mean NCC, **local Gaussian SSIM including covariance**, and symmetric Canny-edge agreement with one-pixel tolerance. The combined score is the minimum of these three metrics, not an average that can hide one poor metric. Each individual threshold and the combined floor must pass. A competing candidate within the configured score gap rejects an otherwise passing match as ambiguous. These are image-similarity scores, not calibrated probabilities.

Defaults: score/NCC 0.8, SSIM 0.75, edge 0.65; relative angle ±10°, scale 0.95–1.05; KNN ratio 0.75, 12 matches, 10 inliers, inlier ratio 0.65, source reprojection tolerance 3 px, 5000 RANSAC iterations, confidence 0.999, coverage 0.15, valid pixels 0.98, ambiguity gap 0.05. Full-resolution feature processing is default; optional feature resize maps measurements back to source pixels. The combined score floor can be stricter than an individual threshold.

These defaults are starting points, not production acceptance thresholds. Narrow the search ROI and plausible angle/scale ranges before considering resize. Accuracy is favored over latency; full-resolution multi-pose Normal matching can be expensive. Range validation caps each coarse/fine sweep at 10000 candidates. Native work already executing is not interrupted mid-OpenCV call; Stop/cancellation discards its eventual result and prevents publication into a new cycle.

## Persistence and evidence

Schema v3 adds per-end `TerminalTemplate`: enable flag, stable numeric algorithm ID, all per-algorithm profiles, source PNG dimensions, learning ROI and runtime search ROI. Numeric parameter order is part of matching C ABI v1 and must not be reordered. The original OCR ABI remains v1 unchanged.

The recipe store writes immutable `terminal1/2-<generation>.png` alongside immutable OCR references, then atomically publishes JSON. File references must be local basenames. PNG signature/dimensions and ROI/profile ranges are checked; missing template assets exclude that recipe from the catalog with a visible load error. Old generations remain recoverable. Loading and RUN snapshot copy PNG bytes, both ROIs and all profile dictionaries so later draft edits cannot change an active inspection.

RUN displays learned/aligned images and source-coordinate outline, reason code and metrics. The persisted end result includes those PNGs as base64 plus corners, algorithm, pose and elapsed matching time. `result.json` contains the recipe configuration; PPM files retain original frames. No image retention policy is added here.

## Diagnostic evidence — 2026-09-04

Read-only replay of `model4/v8`, cycle `d37ae96e234c43c491c13578f32dbe0e`, using its saved 4024×3036 end frames. The current saved end configuration was checked equal to the embedded cycle configuration. SIFT was selected in the screenshot; AKAZE was replayed on the same data with its stored default profile.

| Algorithm/end | Reciprocal matches / inliers | Cause | Diagnostic appearance |
|---|---|---|---|
| SIFT / 1 | 38 / 24 | Inlier ratio 63.16% < 65%; combined score also below 0.8 | NCC .98895; SSIM .91150; edge/score .69364 |
| SIFT / 2 | 12 / 4 | Inliers < 10; invalid transform | N/A — cannot align validly |
| AKAZE / 1 | 70 / 57 | Combined score .77249 < .8 | NCC .99144; SSIM .91888; edge/score .77249 |
| AKAZE / 2 | 4 / 4 | Matches < 12; invalid transform | N/A — cannot align validly |

The old code stopped at the first failed feature gate and lost these counts. The new code retains them. With at least four correspondences it can attempt a diagnostic transform and calculate appearance if geometry/valid pixels permit, but **every failed feature gate still forces NG**. This is not an algorithm fallback or threshold relaxation. A failed second attempt is not admitted as a competing instance or allowed to displace a valid first candidate.

`MatchingDiagnostics` is additive result metadata: template/runtime descriptor counts, post-KNN/distance counts, stage availability, independent verification reason, axis scales and the exact threshold array. Existing numeric fields remain compatible; consumers must use availability flags before displaying them. N/A is not a score of zero. Old results without flags are shown as lacking stage diagnostics and should be replayed; no values are inferred or rewritten. Invalid geometry outlines are hidden.

Critical settings are amber-starred with localized impact hints. In particular, `Score = min(NCC, SSIM, Edge)` means Score 0.8 demands all three ≥ 0.8 even if the individual Edge threshold is 0.65. Default/saved profiles are intentionally unchanged; calibration requires independently labeled samples, not lowering thresholds until these two frames pass. Weak keypoint counts, repeated backgrounds and glare should be reviewed alongside a focused search ROI and a distinctive taught region.

Read-only replay tool (build native for the configuration first):

    dotnet run --project tools/MatchingReplay -c Release -- "path/to/recipe.json" "path/to/result-folder" Sift

Optional `Parameter=value` arguments apply only to an in-memory copy for experiments. The tool reads the app's P6 end1/end2.ppm files, uses the production managed/native matcher, writes JSON evidence to stdout and never contacts hardware or saves a recipe/result. Results from this probe are not production acceptance records.

## Verification limits

Automated fixtures cover all five positive paths, different templates, forbidden reflection/rotation, allowed 180° Normal matching, repeated competing candidates, full-image/circle masks, excluded search regions, empty/corrupt assets, immutable storage, combined verdicts, cancellation, default/legacy behavior and language/profile stability. WPF smoke opens the real teaching window and renders RUN evidence. Fixtures are synthetic and do not establish accuracy on real terminals.

Before production use, validate every selected algorithm/profile against independent OK/NG images covering wrong terminal types, reversed terminals, repeated shapes, partial occlusion, glare, exposure variation, blur and expected position/angle/scale variation. Confirm false-accept rate first, then false rejects and cycle time on the actual camera/PC. The historical OCR 26-image dataset is still incomplete in its external folder; it was not revalidated by this matching work. No hardware or PLC output is exercised by automated tests.
